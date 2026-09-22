using System.Text.Json;

namespace Rift.Core;

public sealed record OverlayPlayer(string RiotId, string Team, string Champion, int Level, string Role, int Keystone, string[] Spells)
{
    // Live Client practice rosters identify generated opponents with the #BOT tag.
    public bool IsBot => RiotId.EndsWith("#BOT", StringComparison.OrdinalIgnoreCase);
    public bool CanLookup => !IsBot && RiotId.LastIndexOf('#') is var split && split > 0 && split < RiotId.Length - 1;
    public static IReadOnlyList<OverlayPlayer> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new JsonException("Liste de joueurs indisponible.");
        var result = new List<OverlayPlayer>();
        foreach (var p in root.EnumerateArray().Take(20))
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            var id = DraftParser.Text(p, "riotId");
            if (id.Length == 0)
            {
                var name = DraftParser.Text(p, "riotIdGameName"); var tag = DraftParser.Text(p, "riotIdTagLine");
                if (name.Length > 0 && tag.Length > 0) id = name + "#" + tag;
            }
            // Never recover an identity from a masked summonerName or any other hidden source.
            var code = DraftParser.Text(p, "rawChampionName");
            const string prefix = "game_character_displayname_";
            code = code.StartsWith(prefix) ? code[prefix.Length..] : DraftParser.Text(p, "championName");
            int rune = 0;
            if (p.TryGetProperty("runes", out var runes) && runes.ValueKind == JsonValueKind.Object && runes.TryGetProperty("keystone", out var key)) rune = DraftParser.Integer(key, "id");
            var spells = new List<string>();
            if (p.TryGetProperty("summonerSpells", out var ss) && ss.ValueKind == JsonValueKind.Object)
                foreach (var spell in ss.EnumerateObject())
                {
                    var raw = DraftParser.Text(spell.Value, "rawDisplayName");
                    spells.Add(raw.Replace("GeneratedTip_SummonerSpell_", "").Replace("_DisplayName", ""));
                }
            result.Add(new(id, DraftParser.Text(p, "team"), code, Math.Max(0, DraftParser.Integer(p, "level")),
                DraftParser.Text(p, "position").ToLowerInvariant() switch { "mid" => "middle", "bot" => "bottom", "support" => "utility", var role => role }, rune, spells.Take(2).ToArray()));
        }
        return result.OrderBy(p => p.Team == "ORDER" ? 0 : 1).ThenBy(p => p.Team).ThenBy(p => Array.IndexOf(new[] { "top", "jungle", "middle", "bottom", "utility", "" }, p.Role)).ToArray();
    }
}

public sealed record OverlayStats(string ChampionStats, string Recent12Hours, string Recent30Days, string MainRole, string[] Tags)
{
    public string Kills { get; init; } = "—";
    public string Deaths { get; init; } = "—";
    public string Assists { get; init; } = "—";
    public static OverlayStats From(PlayerProfile profile, string champion, DateTimeOffset now)
    {
        var matches = profile.Matches.Where(m => !m.Remake && m.Seconds > 0 && m.PlayedAt <= now).ToArray();
        var chosen = matches.Where(m => m.Champion == champion).ToArray();
        string Period(TimeSpan duration)
        {
            var rows = matches.Where(m => now - m.PlayedAt <= duration).ToArray();
            return rows.Length == 0 ? "Aucune dans l’échantillon" : $"{rows.Count(m => m.Win) * 100.0 / rows.Length:F0} % · {rows.Length} parties\n{rows.Count(m => m.Win)} victoires";
        }
        var sum = ProfileSummary.From(chosen);
        var role = matches.Where(m => m.Role.Length > 0 && QueueCatalog.HasStandardRoles(m.Queue)).GroupBy(m => m.Role).OrderByDescending(g => g.Count()).FirstOrDefault();
        var tags = new List<string>();
        if (chosen.Length >= 3)
        {
            if (sum.CsPerMinute >= 7) tags.Add($"Farm ≥ 7 CS/min · {chosen.Length} parties");
            if (sum.Participation >= 65) tags.Add($"Participation ≥ 65 % · {chosen.Length} parties");
            if (sum.WinRate >= 70) tags.Add($"≥ 70 % victoires · {chosen.Length} parties");
        }
        return new(chosen.Length == 0 ? "Champion absent de l’échantillon" : $"{sum.WinRate:F0} % · {chosen.Length} parties sur ce champion",
            Period(TimeSpan.FromHours(12)), Period(TimeSpan.FromDays(30)), role is null ? "Non établi" : Roles.Labels.GetValueOrDefault(role.Key, role.Key), tags.ToArray())
            { Kills = chosen.Length == 0 ? "—" : sum.Kills.ToString("F1"), Deaths = chosen.Length == 0 ? "—" : sum.Deaths.ToString("F1"), Assists = chosen.Length == 0 ? "—" : sum.Assists.ToString("F1") };
    }
}
