using System.Text.Json;

namespace Rift.Core;

public sealed record RankEntry(string Queue, string Tier, string Division, int Lp, int Wins, int Losses);
public sealed record ProfileMatch(string Id, string Champion, int Queue, string Role, bool Win, bool Remake,
    DateTimeOffset PlayedAt, double Seconds, int Kills, int Deaths, int Assists, int Cs, int Damage,
    int Gold, int Vision, int WardsPlaced, int WardsKilled, int ControlWards, int TeamKills)
{
    public int ChampionId { get; init; }
    public int[] Items { get; init; } = [];
    public string QueueLabel => QueueCatalog.Label(Queue);
    public string RoleLabel => Roles.Labels.GetValueOrDefault(Role, QueueCatalog.HasStandardRoles(Queue) ? "Rôle non fourni par Riot" : "Sans rôle standard");
    public string Result => Remake ? "Remake" : Win ? "Victoire" : "Défaite";
    public string KdaLine => $"{Kills} / {Deaths} / {Assists}";
    public string PlayedLabel => $"{PlayedAt.ToLocalTime():dd/MM HH:mm} · {(int)(Seconds / 60)}:{(int)(Seconds % 60):00}";
    public double? Participation => TeamKills > 0 ? 100.0 * (Kills + Assists) / TeamKills : null;
}
public sealed record PlayerProfile(string RiotId, string Platform, int Level, IReadOnlyList<RankEntry> Ranks,
    IReadOnlyList<ProfileMatch> Matches, DateTimeOffset LoadedAt, bool Demo, int RequestedMatches, string Notice = "")
{
    public string Puuid { get; init; } = "";
    public int? ProfileIconId { get; init; }
    public int NextStart { get; init; }
    public long HistoryEndTime { get; init; }
    public bool HasMore { get; init; }
}
public sealed record ProfileSummary(int Games, int Wins, double Kda, double CsPerMinute, double DamagePerMinute,
    double GoldPerMinute, double Vision, double? Participation, double Kills, double Deaths, double Assists,
    double WardsPlaced, double WardsKilled, double ControlWards)
{
    public double WinRate => Games == 0 ? 0 : 100.0 * Wins / Games;
    public static ProfileSummary From(IEnumerable<ProfileMatch> input)
    {
        var rows = input.Where(x => !x.Remake && x.Seconds > 0).ToArray();
        var minutes = rows.Sum(x => x.Seconds) / 60;
        var kp = rows.Where(x => x.Participation.HasValue).Select(x => x.Participation!.Value).ToArray();
        double Mean(Func<ProfileMatch, int> get) => rows.Length == 0 ? 0 : rows.Average(get);
        return new(rows.Length, rows.Count(x => x.Win), rows.Sum(x => x.Kills + x.Assists) / (double)Math.Max(1, rows.Sum(x => x.Deaths)),
            minutes == 0 ? 0 : rows.Sum(x => x.Cs) / minutes, minutes == 0 ? 0 : rows.Sum(x => x.Damage) / minutes,
            minutes == 0 ? 0 : rows.Sum(x => x.Gold) / minutes, Mean(x => x.Vision), kp.Length == 0 ? null : kp.Average(),
            Mean(x => x.Kills), Mean(x => x.Deaths), Mean(x => x.Assists), Mean(x => x.WardsPlaced), Mean(x => x.WardsKilled), Mean(x => x.ControlWards));
    }
}

public static class ProfileParser
{
    public static ProfileMatch? Match(JsonElement root, string puuid)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object || !info.TryGetProperty("participants", out var players) || players.ValueKind != JsonValueKind.Array) return null;
        var p = players.EnumerateArray().FirstOrDefault(x => DraftParser.Text(x, "puuid") == puuid);
        if (p.ValueKind != JsonValueKind.Object) return null;
        int N(string key) => Math.Max(0, DraftParser.Integer(p, key));
        int queue = DraftParser.Integer(info, "queueId");
        if (queue == 0) return null; // Custom matches are not part of public profile history.
        double seconds = info.TryGetProperty("gameDuration", out var duration) && duration.ValueKind == JsonValueKind.Number && duration.TryGetDouble(out var d) ? d : 0;
        // Recent Match-v5 gameDuration is in seconds. Timestamp delta is authoritative when present.
        if (info.TryGetProperty("gameStartTimestamp", out var start) && start.ValueKind == JsonValueKind.Number && start.TryGetInt64(out var startMs) &&
            info.TryGetProperty("gameEndTimestamp", out var end) && end.ValueKind == JsonValueKind.Number && end.TryGetInt64(out var endMs) && endMs > startMs) seconds = ((double)endMs - startMs) / 1000.0;
        if (!double.IsFinite(seconds) || seconds <= 0) return null;
        long timestamp = info.TryGetProperty("gameCreation", out var created) && created.ValueKind == JsonValueKind.Number && created.TryGetInt64(out var ms) ? ms : 0;
        if (timestamp < -62135596800000L || timestamp > 253402300799999L) return null;
        var team = DraftParser.Integer(p, "teamId");
        var position = DraftParser.Text(p, "teamPosition");
        if (position is not ("TOP" or "JUNGLE" or "MIDDLE" or "BOTTOM" or "UTILITY")) position = DraftParser.Text(p, "individualPosition");
        var role = position switch { "TOP" => "top", "JUNGLE" => "jungle", "MIDDLE" => "middle", "BOTTOM" => "bottom", "UTILITY" => "utility", _ => "" };
        if (DraftParser.Integer(info, "mapId") != 11 || !QueueCatalog.HasStandardRoles(queue)) role = "";
        bool Flag(string key) => p.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
        return new(root.TryGetProperty("metadata", out var metadata) ? DraftParser.Text(metadata, "matchId") : "",
            DraftParser.Text(p, "championName"), queue, role, Flag("win"), Flag("gameEndedInEarlySurrender"),
            DateTimeOffset.FromUnixTimeMilliseconds(timestamp), seconds, N("kills"), N("deaths"), N("assists"),
            N("totalMinionsKilled") + N("neutralMinionsKilled"), N("totalDamageDealtToChampions"), N("goldEarned"),
            N("visionScore"), N("wardsPlaced"), N("wardsKilled"), N("visionWardsBoughtInGame"),
            players.EnumerateArray().Where(x => DraftParser.Integer(x, "teamId") == team).Sum(x => DraftParser.Integer(x, "kills")))
        { ChampionId = N("championId"), Items = Enumerable.Range(0, 7).Select(i => N($"item{i}")).ToArray() };
    }
}
