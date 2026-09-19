using System.Text.Json;
using Microsoft.Data.Sqlite;
using Rift.Core;

namespace Rift.Infrastructure;

// Stores only the selected player's normalized match, not the full 10-player API response.
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
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS profile_matches_v1 (cache_key TEXT PRIMARY KEY, json TEXT NOT NULL, saved INTEGER NOT NULL); DELETE FROM profile_matches_v1 WHERE saved < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()); cmd.ExecuteNonQuery();
    }
    public ProfileMatch? Get(string key)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json FROM profile_matches_v1 WHERE cache_key=$key AND saved >= $cutoff";
        cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds());
        var json = cmd.ExecuteScalar() as string;
        try { return json is null ? null : JsonSerializer.Deserialize<ProfileMatch>(json); } catch (JsonException) { return null; }
    }
    public void Set(string key, ProfileMatch match)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO profile_matches_v1 VALUES($key,$json,$saved) ON CONFLICT(cache_key) DO UPDATE SET json=excluded.json,saved=excluded.saved";
        cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(match));
        cmd.Parameters.AddWithValue("$saved", DateTimeOffset.UtcNow.ToUnixTimeSeconds()); cmd.ExecuteNonQuery();
    }
}
