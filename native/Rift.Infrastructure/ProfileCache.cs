using System.Text.Json;
using Microsoft.Data.Sqlite;
using Rift.Core;

namespace Rift.Infrastructure;

// Normalized player summaries and match scoreboards; no API keys or raw responses.
public sealed class ProfileCache(string path)
{
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); c.Open(); return c;
    }
    public void Initialize()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS profile_matches_v1 (cache_key TEXT PRIMARY KEY, json TEXT NOT NULL, saved INTEGER NOT NULL); DELETE FROM profile_matches_v1 WHERE saved < $cutoff; CREATE TABLE IF NOT EXISTS match_details_v1 (cache_key TEXT PRIMARY KEY, json TEXT NOT NULL, saved INTEGER NOT NULL); DELETE FROM match_details_v1 WHERE saved < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()); cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS player_ranks_v1 (cache_key TEXT PRIMARY KEY, json TEXT NOT NULL, saved INTEGER NOT NULL); DELETE FROM player_ranks_v1 WHERE saved < $cutoff";
        cmd.ExecuteNonQuery();
    }
    public ProfileMatch? Get(string key)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json FROM profile_matches_v1 WHERE cache_key=$key AND saved >= $cutoff";
        cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds());
        var json = cmd.ExecuteScalar() as string;
        RuntimeDiagnostics.Count(json is null ? "SQLite · absences" : "SQLite · entrées trouvées");
        try { return json is null ? null : JsonSerializer.Deserialize<ProfileMatch>(json); } catch (JsonException) { return null; }
    }
    public void Set(string key, ProfileMatch match)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO profile_matches_v1 VALUES($key,$json,$saved) ON CONFLICT(cache_key) DO UPDATE SET json=excluded.json,saved=excluded.saved";
        cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(match));
        cmd.Parameters.AddWithValue("$saved", DateTimeOffset.UtcNow.ToUnixTimeSeconds()); cmd.ExecuteNonQuery();
    }
    public MatchDetails? GetDetails(string region, string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json FROM match_details_v1 WHERE cache_key=$key AND saved >= $cutoff";
        cmd.Parameters.AddWithValue("$key", $"{region}:{id}");
        cmd.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds());
        var json = cmd.ExecuteScalar() as string;
        RuntimeDiagnostics.Count(json is null ? "SQLite · absences" : "SQLite · entrées trouvées");
        try { return json is null ? null : JsonSerializer.Deserialize<MatchDetails>(json); } catch (JsonException) { return null; }
    }
    public void SetDetails(string region, MatchDetails details)
    {
        using var operation = RuntimeDiagnostics.Begin("SQLite", "Écriture détail de partie");
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO match_details_v1 VALUES($key,$json,$saved) ON CONFLICT(cache_key) DO UPDATE SET json=excluded.json,saved=excluded.saved";
        cmd.Parameters.AddWithValue("$key", $"{region}:{details.Id}"); cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(details));
        cmd.Parameters.AddWithValue("$saved", DateTimeOffset.UtcNow.ToUnixTimeSeconds()); cmd.ExecuteNonQuery();
    }
    public PlayerRanks? GetRanks(string platform, string puuid)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json FROM player_ranks_v1 WHERE cache_key=$key";
        cmd.Parameters.AddWithValue("$key", $"{platform}:{puuid}");
        try { return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<PlayerRanks>(json) : null; } catch (JsonException) { return null; }
    }
    public void SetRanks(string platform, string puuid, PlayerRanks ranks)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO player_ranks_v1 VALUES($key,$json,$saved) ON CONFLICT(cache_key) DO UPDATE SET json=excluded.json,saved=excluded.saved";
        cmd.Parameters.AddWithValue("$key", $"{platform}:{puuid}"); cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(ranks));
        cmd.Parameters.AddWithValue("$saved", ranks.FetchedAt.ToUnixTimeSeconds()); cmd.ExecuteNonQuery();
    }
}
