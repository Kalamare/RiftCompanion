using Microsoft.Data.Sqlite;
using Rift.Core;

namespace Rift.Infrastructure;

// Short-lived connections. The UI calls these synchronous SQLite operations on a worker thread.
public sealed class SettingsStore(string file)
{
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
    public void Initialize()
    {
        using var c = Open();
        using var version = c.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var current = Convert.ToInt32(version.ExecuteScalar());
        if (current > 1) throw new InvalidOperationException("Cette base nécessite une version plus récente de l’application.");
        using var transaction = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL); PRAGMA user_version = 1;";
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }
    public string? Get(string key)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }
    public void Set(string key, string value)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key, value) VALUES($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }
    public string PreferredRole => Roles.Validate(Get("preferredRole"));
}
