using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Rift.Backend;

public sealed record BackendOptions(string ConnectionString, string RiotKey, string AccessKey, bool FixtureMode, DateTimeOffset HistoryFrom)
{
    public int RefreshCooldownSeconds { get; init; } = 300;
    public bool HistoryBackfillEnabled { get; init; }
    public static BackendOptions Load(IConfiguration config)
    {
        string Secret(string name) => config[name + "_FILE"] is { Length: > 0 } path ? File.ReadAllText(path).Trim() : config[name] ?? "";
        var connection = config["RIFT_DATABASE"];
        if (string.IsNullOrWhiteSpace(connection))
        {
            var password = Secret("POSTGRES_PASSWORD");
            if (password.Length < 16) throw new InvalidOperationException("Configure POSTGRES_PASSWORD_FILE (au moins 16 caractères).");
            var settings = new NpgsqlConnectionStringBuilder { Host = config["POSTGRES_HOST"] ?? "postgres", Database = "rift", Username = "rift", Password = password, IncludeErrorDetail = false };
            settings["GSS Encryption Mode"] = "Disable";
            connection = settings.ConnectionString;
        }
        var access = Secret("RIFT_ACCESS_KEY");
        if (access.Length < 32) throw new InvalidOperationException("Configure RIFT_ACCESS_KEY_FILE (au moins 32 caractères).");
        bool fixture = bool.TryParse(config["RIFT_FIXTURE_MODE"], out var enabled) && enabled;
        var date = config["RIFT_HISTORY_FROM"] ?? "2026-01-08T00:00:00Z";
        if (!DateTimeOffset.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            throw new InvalidOperationException("RIFT_HISTORY_FROM est invalide.");
        var from = parsed.ToUniversalTime();
        if (from.Year < 2021 || from >= DateTimeOffset.UtcNow || from.TimeOfDay != TimeSpan.Zero) throw new InvalidOperationException("RIFT_HISTORY_FROM doit être une date passée à minuit UTC.");
        var cooldown = int.TryParse(config["RIFT_REFRESH_COOLDOWN_SECONDS"] ?? "300", out var seconds) && seconds is >= 120 and <= 3600
            ? seconds : throw new InvalidOperationException("RIFT_REFRESH_COOLDOWN_SECONDS doit être compris entre 120 et 3600.");
        return new(connection, Secret("RIOT_API_KEY"), access, fixture, from) { RefreshCooldownSeconds = cooldown,
            HistoryBackfillEnabled = bool.TryParse(config["RIFT_HISTORY_BACKFILL"], out var backfill) && backfill };
    }
}
