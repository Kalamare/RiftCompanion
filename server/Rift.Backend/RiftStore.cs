using System.Data;
using System.Text.Json;
using Npgsql;
using Rift.Contracts;
using Rift.Core;

namespace Rift.Backend;

public sealed partial class RiftStore(NpgsqlDataSource source, BackendOptions? options = null)
{
    public bool HistoryBackfillEnabled => options?.HistoryBackfillEnabled == true;
    internal static NpgsqlCommand Command(NpgsqlConnection connection, string sql, params object?[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var value in parameters) command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        return command;
    }
    public async Task Initialize(CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await using (var mutex = Command(connection, "SELECT pg_advisory_xact_lock(84723001)")) await mutex.ExecuteNonQueryAsync(token);
        await using (var exists = Command(connection, "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema=current_schema() AND table_name='schema_version')"))
            if ((bool)(await exists.ExecuteScalarAsync(token))!)
            {
                await using var version = Command(connection, "SELECT coalesce(max(version),0) FROM schema_version");
                if ((int)(await version.ExecuteScalarAsync(token))! > 2) throw new InvalidOperationException("Schéma serveur plus récent que cet exécutable.");
            }
        using var stream = typeof(RiftStore).Assembly.GetManifestResourceStream("Rift.Backend.schema.sql")!;
        using var reader = new StreamReader(stream);
        await using var command = Command(connection, await reader.ReadToEndAsync(token));
        await command.ExecuteNonQueryAsync(token); await tx.CommitAsync(token);
    }
    public async Task<Guid> Lookup(ProfileLookup lookup, DateTimeOffset from, CancellationToken token, string actor = "private")
    {
        lookup = ProfileInput.Normalize(lookup);
        await using var connection = await source.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await using (var gate = Command(connection, "SELECT pg_advisory_xact_lock(84723004)")) await gate.ExecuteNonQueryAsync(token);
        await using (var existing = Command(connection, "SELECT id FROM profile_jobs WHERE platform=$1 AND normalized_id=$2 AND kind='history'", lookup.Platform, lookup.RiotId.ToUpperInvariant()))
        {
            if (await existing.ExecuteScalarAsync(token) is Guid id)
            {
                await using var touch = Command(connection, "UPDATE profile_jobs SET active_until=now()+interval '30 minutes' WHERE id=$1 OR (kind='recent' AND platform=$2 AND puuid=(SELECT puuid FROM profile_jobs WHERE id=$1))", id, lookup.Platform);
                await touch.ExecuteNonQueryAsync(token); await tx.CommitAsync(token); return id;
            }
        }
        var retry = await WorkBudget(connection, actor, "lookup", 3, token);
        if (retry is not null) throw new WorkLimit(retry.Value);
        await using var command = Command(connection, """
            INSERT INTO profile_jobs(id,platform,riot_id,normalized_id,active_until,coverage_from,window_from,window_until)
            VALUES($1,$2,$3,$4,now()+interval '30 minutes',$5,$5,date_trunc('second',now()))
            RETURNING id
            """, Guid.NewGuid(), lookup.Platform, lookup.RiotId, lookup.RiotId.ToUpperInvariant(), from);
        var created = (Guid)(await command.ExecuteScalarAsync(token))!;
        await tx.CommitAsync(token); return created;
    }
    public async Task<LookupStatus?> LookupState(Guid id, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        string platform, state; string? puuid, error;
        await using (var command = Command(connection, "SELECT platform,puuid,state,error FROM profile_jobs WHERE id=$1", id))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) return null;
            platform = reader.GetString(0); puuid = reader.IsDBNull(1) ? null : reader.GetString(1); state = reader.GetString(2); error = reader.IsDBNull(3) ? null : reader.GetString(3);
        }
        return new(id, state, error, puuid is null ? null : await Player(platform, puuid, token));
    }
    public async Task<PlayerCard?> Player(string platform, string puuid, CancellationToken token)
    {
        ProfileInput.Player(platform, puuid);
        await using var connection = await source.OpenConnectionAsync(token);
        return await ReadPlayer(connection, platform, puuid, token);
    }
    private static async Task<PlayerCard?> ReadPlayer(NpgsqlConnection connection, string platform, string puuid, CancellationToken token)
    {
        await using var command = Command(connection, """
            SELECT p.riot_id,p.level,p.icon,p.ranks::text,p.ranks_at,p.last_seen,p.version,
              j.coverage_from,GREATEST(j.completed_at,r.completed_at),j.scanned,
              COALESCE(j.missing,0)+COALESCE(r.missing,0),j.state
            FROM players p LEFT JOIN LATERAL
              (SELECT * FROM profile_jobs WHERE platform=p.platform AND puuid=p.puuid AND kind='history' ORDER BY completed_at DESC NULLS LAST LIMIT 1) j ON true
            LEFT JOIN profile_jobs r ON r.platform=p.platform AND r.puuid=p.puuid AND r.kind='recent'
            WHERE p.platform=$1 AND p.puuid=$2
            """, platform, puuid);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        return new(platform, puuid, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetInt32(2),
            JsonSerializer.Deserialize<RankEntry[]>(reader.GetString(3))!, reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5), reader.GetInt64(6),
            new(reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                !reader.IsDBNull(9) && reader.GetBoolean(9), reader.IsDBNull(10) ? 0 : reader.GetInt32(10), reader.IsDBNull(11) ? "encountered_only" : reader.GetString(11)));
    }
    public async Task<bool> HasMatch(string region, string id, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var command = Command(connection, "SELECT EXISTS(SELECT 1 FROM matches WHERE region=$1 AND match_id=$2)", region, id);
        return (bool)(await command.ExecuteScalarAsync(token))!;
    }
    public async Task<bool> Ingest(string region, MatchDetails match, CancellationToken token)
    {
        var platform = match.Id.Split('_')[0].ToLowerInvariant();
        if (!ProfileInput.Regions.TryGetValue(platform, out var expected) || expected != region || match.Participants.Count is < 1 or > 64 ||
            match.Participants.Select(p => p.Puuid).Distinct().Count() != match.Participants.Count ||
            match.Participants.Any(p => p.Stats.Id != match.Id || p.Stats.Queue != match.Queue || p.Stats.PlayedAt != match.PlayedAt))
            throw new ArgumentException("Détail incohérent.");
        foreach (var p in match.Participants) ProfileInput.Player(platform, p.Puuid);
        await using var connection = await source.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await LockChanges(connection, token);
        await using (var insert = Command(connection, "INSERT INTO matches VALUES($1,$2,$3,$4::jsonb) ON CONFLICT DO NOTHING RETURNING match_id", region, match.Id, match.PlayedAt, JsonSerializer.Serialize(match)))
            if (await insert.ExecuteScalarAsync(token) is null) { await tx.RollbackAsync(token); return false; }
        // Deterministic order avoids deadlocks between different matches with overlapping players.
        foreach (var participant in match.Participants.OrderBy(p => p.Puuid, StringComparer.Ordinal))
        {
            var m = participant.Stats;
            await using (var player = Command(connection, """
                INSERT INTO players(platform,puuid,riot_id,last_seen,version) VALUES($1,$2,$3,$4,1)
                ON CONFLICT(platform,puuid) DO UPDATE SET
                  riot_id=CASE WHEN EXCLUDED.last_seen>players.last_seen THEN EXCLUDED.riot_id ELSE players.riot_id END,
                  last_seen=GREATEST(players.last_seen,EXCLUDED.last_seen), version=players.version+1
                """, platform, participant.Puuid, participant.RiotId, match.PlayedAt)) await player.ExecuteNonQueryAsync(token);
            await using (var receipt = Command(connection, "INSERT INTO contributions VALUES($1,$2,$3,$4)", region, match.Id, platform, participant.Puuid)) await receipt.ExecuteNonQueryAsync(token);
            if (!m.Remake && m.Seconds > 0)
            {
                await using var stats = Command(connection, """
                    INSERT INTO daily_stats VALUES($1,$2,$3,$4,$5,$6,$7,1,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21)
                    ON CONFLICT(platform,puuid,day,queue,champion_id,role) DO UPDATE SET
                      games=daily_stats.games+1,wins=daily_stats.wins+EXCLUDED.wins,
                      kills=daily_stats.kills+EXCLUDED.kills,deaths=daily_stats.deaths+EXCLUDED.deaths,assists=daily_stats.assists+EXCLUDED.assists,
                      cs=daily_stats.cs+EXCLUDED.cs,damage=daily_stats.damage+EXCLUDED.damage,gold=daily_stats.gold+EXCLUDED.gold,
                      vision=daily_stats.vision+EXCLUDED.vision,wards=daily_stats.wards+EXCLUDED.wards,wards_killed=daily_stats.wards_killed+EXCLUDED.wards_killed,
                      control_wards=daily_stats.control_wards+EXCLUDED.control_wards,seconds=daily_stats.seconds+EXCLUDED.seconds,
                      kp_sum=daily_stats.kp_sum+EXCLUDED.kp_sum,kp_count=daily_stats.kp_count+EXCLUDED.kp_count
                    """, platform, participant.Puuid, DateOnly.FromDateTime(m.PlayedAt.UtcDateTime), m.Queue, m.ChampionId, m.Champion,
                    QueueCatalog.HasStandardRoles(m.Queue) ? m.Role : "", m.Win ? 1L : 0L, (long)m.Kills, (long)m.Deaths, (long)m.Assists,
                    (long)m.Cs, (long)m.Damage, (long)m.Gold, (long)m.Vision, (long)m.WardsPlaced, (long)m.WardsKilled, (long)m.ControlWards,
                    m.Seconds, m.Participation ?? 0, m.Participation.HasValue ? 1L : 0L);
                await stats.ExecuteNonQueryAsync(token);
            }
            await Change(connection, platform, participant.Puuid, token);
        }
        await tx.CommitAsync(token); return true;
    }
    internal static async Task Change(NpgsqlConnection connection, string platform, string puuid, CancellationToken token)
    {
        await using var change = Command(connection, "INSERT INTO profile_changes(platform,puuid,version) SELECT platform,puuid,version FROM players WHERE platform=$1 AND puuid=$2", platform, puuid);
        await change.ExecuteNonQueryAsync(token);
    }
    // Serializes aggregate writers in this first deployment, including outbox allocation.
    // Sequence order then also follows commit order; notification cursors cannot skip a late commit.
    internal static async Task LockChanges(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = Command(connection, "SELECT pg_advisory_xact_lock(84723003)");
        await command.ExecuteNonQueryAsync(token);
    }
    public async Task<StatsSnapshot?> Stats(string platform, string puuid, DateTimeOffset from, DateTimeOffset until, int queue, CancellationToken token)
    {
        if (from.TimeOfDay != TimeSpan.Zero || until.TimeOfDay != TimeSpan.Zero || from.Offset != TimeSpan.Zero || until.Offset != TimeSpan.Zero || until <= from || (until - from).TotalDays > 1096 || queue < -1)
            throw new ArgumentException("La période doit couvrir des journées UTC, avec un maximum de trois ans.");
        ProfileInput.Player(platform, puuid);
        await using var connection = await source.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var player = await ReadPlayer(connection, platform, puuid, token); if (player is null) return null;
        await using var command = Command(connection, """
            SELECT queue,champion_id,champion,role,sum(games)::bigint,sum(wins)::bigint,sum(kills)::bigint,sum(deaths)::bigint,sum(assists)::bigint,
              sum(cs)::bigint,sum(damage)::bigint,sum(gold)::bigint,sum(vision)::bigint,sum(wards)::bigint,sum(wards_killed)::bigint,sum(control_wards)::bigint,
              sum(seconds),sum(kp_sum),sum(kp_count)::bigint FROM daily_stats
            WHERE platform=$1 AND puuid=$2 AND day >= $3 AND day < $4 AND ($5=0 OR queue=$5 OR ($5=-1 AND queue IN (420,440)))
            GROUP BY queue,champion_id,champion,role
            """, platform, puuid, DateOnly.FromDateTime(from.UtcDateTime), DateOnly.FromDateTime(until.UtcDateTime), queue);
        await using var reader = await command.ExecuteReaderAsync(token); var rows = new List<Totals>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
            reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11),
            reader.GetInt64(12), reader.GetInt64(13), reader.GetInt64(14), reader.GetInt64(15), reader.GetDouble(16), reader.GetDouble(17), reader.GetInt64(18)));
        return new(platform, puuid, from, until, queue, Totals.Summary(rows),
            rows.GroupBy(x => x.ChampionId).Select(g => new ChampionStats(g.Key, g.First().Champion, Totals.Summary(g))).OrderByDescending(x => x.Summary.Games).ToArray(),
            rows.Where(x => QueueCatalog.HasStandardRoles(x.Queue)).GroupBy(x => x.Role).Select(g => new RoleStats(g.Key, Totals.Summary(g))).OrderByDescending(x => x.Summary.Games).ToArray(),
            player.Version, player.Coverage);
    }
    public async Task<ProfileChanged[]> Changes(long after, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var command = Command(connection, "SELECT sequence,platform,puuid,version FROM profile_changes WHERE sequence>$1 ORDER BY sequence LIMIT 100", after);
        await using var reader = await command.ExecuteReaderAsync(token); var result = new List<ProfileChanged>();
        while (await reader.ReadAsync(token)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
        return result.ToArray();
    }
}
