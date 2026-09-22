using System.Text.Json;
using Npgsql;
using Rift.Contracts;
using Rift.Core;

namespace Rift.Backend;

public sealed record ProfileJob(Guid Id, string Platform, string RiotId, string? Puuid, int Cursor, DateTimeOffset From, DateTimeOffset Until, string Kind = "history");
public sealed record RiotIdentity(string Puuid, string RiotId, int Level, int? Icon, RankEntry[] Ranks);

public sealed partial class RiftStore
{
    public const int HistoryPageSize = 20;
    // One collector leader initially; persistent quotas also survive process restarts.
    public async Task<NpgsqlConnection?> TryCollectorLock(CancellationToken token)
    {
        var connection = await source.OpenConnectionAsync(token);
        await using var command = Command(connection, "SELECT pg_try_advisory_lock(84723002)");
        if ((bool)(await command.ExecuteScalarAsync(token))!) return connection;
        await connection.DisposeAsync(); return null;
    }
    public async Task ReleaseCollectorLock(NpgsqlConnection connection)
    {
        try { await using var command = Command(connection, "SELECT pg_advisory_unlock(84723002)"); await command.ExecuteNonQueryAsync(); }
        finally { await connection.DisposeAsync(); }
    }
    public async Task<ProfileJob?> Claim(CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await using (var gate = Command(connection, "SELECT pg_advisory_xact_lock(84723004)")) await gate.ExecuteNonQueryAsync(token);
        await using var command = Command(connection, """
            WITH turn AS (UPDATE collector_turn SET value=value+1 WHERE id=1 RETURNING value)
            UPDATE profile_jobs SET state='running',lease_until=now()+interval '30 minutes',error=null,
              refresh_after=CASE WHEN kind='recent' AND cursor=0 AND (state='ready' OR refresh_after IS NULL)
                THEN now()+$1 ELSE refresh_after END,
              window_until=CASE WHEN state='ready' THEN date_trunc('second',now()) ELSE window_until END
            WHERE id=(SELECT id FROM profile_jobs WHERE active_until>now() AND due_at<=now()
              AND NOT (kind='history' AND scanned) AND ($2 OR kind='recent' OR puuid IS NULL)
              AND (lease_until IS NULL OR lease_until<now())
              ORDER BY CASE WHEN (SELECT value FROM turn)%4=0 THEN (kind='history') ELSE (puuid IS NULL OR kind='recent') END DESC,
                due_at FOR UPDATE SKIP LOCKED LIMIT 1)
            RETURNING id,platform,riot_id,puuid,cursor,window_from,window_until,kind
            """, TimeSpan.FromSeconds(options?.RefreshCooldownSeconds ?? 300), HistoryBackfillEnabled);
        await using var reader = await command.ExecuteReaderAsync(token);
        ProfileJob? result = await reader.ReadAsync(token) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7)) : null;
        await reader.DisposeAsync(); await tx.CommitAsync(token); return result;
    }
    public async Task SetIdentity(ProfileJob job, RiotIdentity identity, CancellationToken token)
    {
        ProfileInput.Player(job.Platform, identity.Puuid);
        await using var connection = await source.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await using (var gate = Command(connection, "SELECT pg_advisory_xact_lock(84723004)")) await gate.ExecuteNonQueryAsync(token);
        await LockChanges(connection, token);
        await using (var player = Command(connection, """
            INSERT INTO players(platform,puuid,riot_id,level,icon,ranks,ranks_at,last_seen,version) VALUES($1,$2,$3,$4,$5,$6::jsonb,now(),now(),1)
            ON CONFLICT(platform,puuid) DO UPDATE SET riot_id=EXCLUDED.riot_id,level=EXCLUDED.level,icon=EXCLUDED.icon,
              ranks=EXCLUDED.ranks,ranks_at=EXCLUDED.ranks_at,version=players.version+1
            """, job.Platform, identity.Puuid, identity.RiotId, identity.Level, identity.Icon, JsonSerializer.Serialize(identity.Ranks)))
            await player.ExecuteNonQueryAsync(token);
        await using (var update = Command(connection, "UPDATE profile_jobs SET puuid=$2 WHERE id=$1", job.Id, identity.Puuid)) await update.ExecuteNonQueryAsync(token);
        if (job.Kind == "history") await EnsureRecent(connection, job.Platform, identity.Puuid, token);
        await Change(connection, job.Platform, identity.Puuid, token); await tx.CommitAsync(token);
    }
    public async Task CompletePage(ProfileJob job, int count, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await LockChanges(connection, token);
        bool finished = count < HistoryPageSize || job.Kind == "recent" && !HistoryBackfillEnabled;
        await using var command = Command(connection, finished ? """
            UPDATE profile_jobs SET scanned=true,completed_at=window_until,
              window_from=CASE WHEN kind='recent' THEN window_until-interval '48 hours' ELSE window_from END,
              cursor=0,missing=(SELECT count(*) FROM unavailable_matches WHERE job_id=$1),state='ready',error=null,lease_until=null,attempts=0,due_at=now()+interval '15 minutes'
            WHERE id=$1
            """ : """
            UPDATE profile_jobs SET cursor=cursor+20,missing=(SELECT count(*) FROM unavailable_matches WHERE job_id=$1),state='queued',lease_until=null,attempts=0,due_at=now()+interval '1 second' WHERE id=$1
            """, job.Id);
        await command.ExecuteNonQueryAsync(token);
        await using (var version = Command(connection, "UPDATE players SET version=version+1 WHERE platform=$1 AND puuid=$2", job.Platform, job.Puuid)) await version.ExecuteNonQueryAsync(token);
        if (job.Puuid is not null) await Change(connection, job.Platform, job.Puuid, token);
        await tx.CommitAsync(token);
    }
    public async Task SetUnavailable(Guid job, string match, bool unavailable, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var command = Command(connection, unavailable
            ? "INSERT INTO unavailable_matches VALUES($1,$2) ON CONFLICT DO NOTHING"
            : "DELETE FROM unavailable_matches WHERE job_id=$1 AND match_id=$2", job, match);
        await command.ExecuteNonQueryAsync(token);
    }
    public async Task FinishIdentity(ProfileJob job, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var command = Command(connection, "UPDATE profile_jobs SET state='limited',lease_until=null,error=null WHERE id=$1", job.Id);
        await command.ExecuteNonQueryAsync(token);
    }
    public async Task Fail(ProfileJob job, string error, TimeSpan delay, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var command = Command(connection, "UPDATE profile_jobs SET state='paused',error=$2,lease_until=null,attempts=attempts+1,due_at=now()+$3 WHERE id=$1", job.Id, error, delay);
        await command.ExecuteNonQueryAsync(token);
    }
    public async Task<TimeSpan> ReserveRequest(string host, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await using (var mutex = Command(connection, "SELECT pg_advisory_xact_lock(hashtext($1))", "rift-quota:" + host)) await mutex.ExecuteNonQueryAsync(token);
        await using (var trim = Command(connection, "DELETE FROM riot_requests WHERE host=$1 AND at<=now()-interval '1 day'", host)) await trim.ExecuteNonQueryAsync(token);
        await using var next = Command(connection, """
            SELECT GREATEST(now(),COALESCE((SELECT until_at FROM riot_cooldowns WHERE host=$1),now()),
              COALESCE((SELECT at+interval '1 second' FROM riot_requests WHERE host=$1 AND at>now()-interval '1 second' ORDER BY at DESC OFFSET 17 LIMIT 1),now()),
              COALESCE((SELECT at+interval '2 minutes' FROM riot_requests WHERE host=$1 ORDER BY at DESC OFFSET 89 LIMIT 1),now()),
              COALESCE((SELECT max((SELECT at+make_interval(secs => w.seconds) FROM riot_requests WHERE host=$1
                ORDER BY at DESC OFFSET (w.quota-1) LIMIT 1)) FROM riot_windows w WHERE w.host=$1),now()))-now()
            """, host);
        var delay = (TimeSpan)(await next.ExecuteScalarAsync(token))!;
        if (delay <= TimeSpan.Zero) { await using var insert = Command(connection, "INSERT INTO riot_requests(host) VALUES($1)", host); await insert.ExecuteNonQueryAsync(token); }
        await tx.CommitAsync(token); return delay;
    }
    public async Task PauseRequests(string host, TimeSpan delay, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var command = Command(connection, "INSERT INTO riot_cooldowns VALUES($1,now()+$2) ON CONFLICT(host) DO UPDATE SET until_at=GREATEST(riot_cooldowns.until_at,EXCLUDED.until_at)", host, delay);
        await command.ExecuteNonQueryAsync(token);
    }
    public async Task ObserveLimit(string host, int seconds, int quota, int used, CancellationToken token)
    {
        if (seconds is < 1 or > 86400 || quota is < 1 or > 1000000) return;
        await using var connection = await source.OpenConnectionAsync(token);
        // Applying method limits to the whole host is deliberately conservative for one collector.
        await using var command = Command(connection, """
            INSERT INTO riot_windows VALUES($1,$2,$3) ON CONFLICT(host,seconds)
            DO UPDATE SET quota=LEAST(riot_windows.quota,EXCLUDED.quota)
            """, host, seconds, quota);
        await command.ExecuteNonQueryAsync(token);
        if (used >= quota) await PauseRequests(host, TimeSpan.FromSeconds(seconds), token);
    }
}
