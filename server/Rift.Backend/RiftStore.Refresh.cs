using Npgsql;
using Rift.Contracts;

namespace Rift.Backend;

public sealed class WorkLimit(DateTimeOffset retryAt) : Exception("work_budget_exhausted")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}

public sealed partial class RiftStore
{
    // One authenticated private operator today; never accept an actor supplied by the client.
    private static async Task<DateTimeOffset?> WorkBudget(NpgsqlConnection connection, string actor, string kind, int limit, CancellationToken token)
    {
        await using (var trim = Command(connection, "DELETE FROM user_work_requests WHERE at<now()-interval '10 minutes'")) await trim.ExecuteNonQueryAsync(token);
        await using (var check = Command(connection, "SELECT CASE WHEN count(*) >= $3 THEN min(at)+interval '10 minutes' END FROM user_work_requests WHERE actor=$1 AND kind=$2", actor, kind, limit))
            if (await check.ExecuteScalarAsync(token) is DateTime value) return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        await using var add = Command(connection, "INSERT INTO user_work_requests(actor,kind) VALUES($1,$2)", actor, kind);
        await add.ExecuteNonQueryAsync(token); return null;
    }

    private static async Task EnsureRecent(NpgsqlConnection connection, string platform, string puuid, CancellationToken token)
    {
        await using var add = Command(connection, """
            INSERT INTO profile_jobs(id,platform,riot_id,normalized_id,puuid,kind,active_until,coverage_from,window_from,window_until)
            SELECT $1,p.platform,p.riot_id,p.puuid,p.puuid,'recent',now()+interval '30 minutes',
              now()-interval '48 hours',now()-interval '48 hours',date_trunc('second',now())
            FROM players p WHERE p.platform=$2 AND p.puuid=$3
            ON CONFLICT(platform,puuid) WHERE kind='recent' DO NOTHING
            """, Guid.NewGuid(), platform, puuid);
        await add.ExecuteNonQueryAsync(token);
    }

    public async Task Touch(string platform, string puuid, CancellationToken token)
    {
        ProfileInput.Player(platform, puuid);
        await using var connection = await source.OpenConnectionAsync(token);
        // A subscription extends interest only in already scheduled profiles.
        await using var command = Command(connection, "UPDATE profile_jobs SET active_until=now()+interval '30 minutes' WHERE platform=$1 AND puuid=$2", platform, puuid);
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<RefreshStatus?> Refresh(string platform, string puuid, bool request, TimeSpan cooldown, CancellationToken token, string actor = "private")
    {
        ProfileInput.Player(platform, puuid);
        await using var connection = await source.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        // Serializes eligibility + budgets + enqueue across processes and concurrent users.
        await using (var gate = Command(connection, "SELECT pg_advisory_xact_lock(84723004)")) await gate.ExecuteNonQueryAsync(token);
        await using (var player = Command(connection, "SELECT 1 FROM players WHERE platform=$1 AND puuid=$2", platform, puuid))
            if (await player.ExecuteScalarAsync(token) is null) return null;
        string state = "ready"; DateTimeOffset? updated = null, next = null;
        await using (var command = Command(connection, "SELECT state,completed_at,refresh_after,due_at FROM profile_jobs WHERE platform=$1 AND puuid=$2 AND kind='recent' FOR UPDATE", platform, puuid))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
            {
                state = reader.GetString(0);
                updated = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
                next = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
                if (state == "paused") next = reader.GetFieldValue<DateTimeOffset>(3);
            }
        }
        var now = DateTimeOffset.UtcNow;
        var status = state is "queued" or "running" ? "pending" : state == "paused" ? "deferred" : next > now ? "cooldown" : "ready";
        bool accepted = false;
        if (request && status == "ready")
        {
            var budget = await WorkBudget(connection, actor, "refresh", 5, token);
            if (budget is not null) { status = "limited"; next = budget; }
            else
            {
                await EnsureRecent(connection, platform, puuid, token);
                next = now + cooldown;
                await using var queue = Command(connection, """
                    UPDATE profile_jobs SET state='queued',due_at=now(),refresh_after=$3,
                      active_until=now()+interval '30 minutes',window_until=date_trunc('second',now())
                    WHERE platform=$1 AND puuid=$2 AND kind='recent'
                    """, platform, puuid, next);
                await queue.ExecuteNonQueryAsync(token); accepted = true; status = "pending";
            }
        }
        await tx.CommitAsync(token);
        return new(status, now, updated, next, accepted);
    }
}
