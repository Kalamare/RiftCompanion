using System.Text.Json;

namespace Rift.Core;

public sealed record MatchParticipant(string Puuid, string RiotId, int TeamId, int Level, ProfileMatch Stats)
{
    public int[] Spells { get; init; } = [];
    public int? RoleBoundItem { get; init; }
    public MatchRunes Runes { get; init; } = new(0, 0, 0, []);
}
public sealed record MatchRunes(int PrimaryStyle, int Keystone, int SecondaryStyle, int[] Selections)
{
    public int[] PrimarySelections { get; init; } = [];
    public int[] SecondarySelections { get; init; } = [];
}
public sealed record PlayerRanks(IReadOnlyList<RankEntry> Entries, DateTimeOffset FetchedAt);
public sealed record MatchTeam(int Id, bool? Win, int[] Bans, IReadOnlyDictionary<string, int> Objectives);
public sealed record MatchDetails(string Id, int Queue, DateTimeOffset PlayedAt, double Seconds,
    IReadOnlyList<MatchParticipant> Participants, IReadOnlyList<MatchTeam> Teams)
{
    public int SchemaVersion { get; init; }
    public static MatchDetails? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("info", out var info) ||
            info.ValueKind != JsonValueKind.Object || !info.TryGetProperty("participants", out var players) || players.ValueKind != JsonValueKind.Array) return null;
        var participants = new List<MatchParticipant>();
        foreach (var player in players.EnumerateArray())
        {
            if (player.ValueKind != JsonValueKind.Object) continue;
            var puuid = DraftParser.Text(player, "puuid");
            if (puuid.Length == 0 || participants.Any(x => x.Puuid == puuid)) continue;
            var stats = ProfileParser.Match(root, puuid);
            if (stats is null) continue;
            var name = DraftParser.Text(player, "riotIdGameName");
            var tag = DraftParser.Text(player, "riotIdTagline");
            var subteam = DraftParser.Integer(player, "playerSubteamId");
            participants.Add(new(puuid, name.Length == 0 ? "Joueur non identifié" : name + (tag.Length == 0 ? "" : "#" + tag),
                subteam > 0 ? subteam : DraftParser.Integer(player, "teamId"), Math.Max(0, DraftParser.Integer(player, "champLevel")), stats)
                {
                    Spells = [Math.Max(0, DraftParser.Integer(player, "summoner1Id")), Math.Max(0, DraftParser.Integer(player, "summoner2Id"))],
                    RoleBoundItem = player.TryGetProperty("roleBoundItem", out var quest) && quest.ValueKind == JsonValueKind.Number && quest.TryGetInt32(out var questId) && questId >= 0 ? questId : null,
                    Runes = ParseRunes(player)
                });
        }
        if (participants.Count == 0 || participants[0].Stats.Id.Length == 0) return null;
        bool subteams = players.EnumerateArray().Any(p => DraftParser.Integer(p, "playerSubteamId") > 0);
        if (subteams)
            participants = participants.Select(p => p with { Stats = p.Stats with { TeamKills = participants.Where(t => t.TeamId == p.TeamId).Sum(t => t.Stats.Kills) } }).ToList();
        var teams = new List<MatchTeam>();
        foreach (var group in participants.GroupBy(p => p.TeamId))
        {
            JsonElement team = default;
            if (!subteams && info.TryGetProperty("teams", out var teamArray) && teamArray.ValueKind == JsonValueKind.Array)
                team = teamArray.EnumerateArray().FirstOrDefault(t => DraftParser.Integer(t, "teamId") == group.Key);
            var objectives = new Dictionary<string, int>();
            var bans = new List<int>();
            bool? win = null;
            if (team.ValueKind == JsonValueKind.Object)
            {
                if (team.TryGetProperty("win", out var won) && won.ValueKind is JsonValueKind.True or JsonValueKind.False) win = won.GetBoolean();
                if (team.TryGetProperty("bans", out var banned) && banned.ValueKind == JsonValueKind.Array)
                    bans.AddRange(banned.EnumerateArray().OrderBy(b => DraftParser.Integer(b, "pickTurn")).Select(b => DraftParser.Integer(b, "championId")));
                if (team.TryGetProperty("objectives", out var goals) && goals.ValueKind == JsonValueKind.Object)
                    foreach (var goal in goals.EnumerateObject())
                        if (goal.Value.ValueKind == JsonValueKind.Object && goal.Value.TryGetProperty("kills", out var kills) && kills.ValueKind == JsonValueKind.Number && kills.TryGetInt32(out var count) && count >= 0)
                            objectives[goal.Name] = count;
            }
            teams.Add(new(group.Key, win, bans.ToArray(), objectives));
        }
        var first = participants[0].Stats;
        return new(first.Id, first.Queue, first.PlayedAt, first.Seconds, participants, teams) { SchemaVersion = 4 };
    }
    private static MatchRunes ParseRunes(JsonElement player)
    {
        int primary = 0, keystone = 0, secondary = 0;
        var selections = new List<int>();
        int[] primarySelections = [], secondarySelections = [];
        if (player.TryGetProperty("perks", out var perks) && perks.ValueKind == JsonValueKind.Object &&
            perks.TryGetProperty("styles", out var styles) && styles.ValueKind == JsonValueKind.Array)
            foreach (var style in styles.EnumerateArray())
            {
                if (style.ValueKind != JsonValueKind.Object) continue;
                bool isPrimary = DraftParser.Text(style, "description") == "primaryStyle";
                if (isPrimary) primary = Math.Max(0, DraftParser.Integer(style, "style"));
                else if (DraftParser.Text(style, "description") == "subStyle") secondary = Math.Max(0, DraftParser.Integer(style, "style"));
                if (style.TryGetProperty("selections", out var chosen) && chosen.ValueKind == JsonValueKind.Array)
                {
                    var ids = chosen.EnumerateArray().Select(r => DraftParser.Integer(r, "perk")).Where(id => id > 0).ToArray();
                    if (isPrimary) { keystone = ids.FirstOrDefault(); primarySelections = ids; }
                    else if (DraftParser.Text(style, "description") == "subStyle") secondarySelections = ids;
                    selections.AddRange(ids);
                }
            }
        return new(primary, keystone, secondary, selections.ToArray()) { PrimarySelections = primarySelections, SecondarySelections = secondarySelections };
    }
}
