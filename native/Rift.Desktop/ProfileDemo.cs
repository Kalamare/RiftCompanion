using Rift.Core;
namespace Rift.Desktop;

internal static class ProfileDemo
{
    public static MatchDetails Details(ProfileMatch match)
    {
        string[] names = ["Ornn", "Vi", "Ahri", "Jinx", "Thresh", "Garen", "Viego", "Lux", "Caitlyn", "Leona"];
        int[] ids = [516, 254, 103, 222, 412, 86, 234, 99, 51, 89];
        string[] roles = ["top", "jungle", "middle", "bottom", "utility"];
        int selected = Array.IndexOf(roles, match.Role); if (selected < 0) selected = 0;
        var players = Enumerable.Range(0, 10).Select(i => new MatchParticipant(i == selected ? "demo-selected" : $"demo-{i}",
            i == selected ? "Invocateur#DEMO" : $"Joueur{i + 1}#DEMO", i < 5 ? 100 : 200, 13 + i % 5,
            i == selected ? match : match with { Champion = names[i], ChampionId = ids[i], Role = roles[i % 5], Kills = 2 + i, Deaths = 1 + i % 6,
                Assists = 3 + i % 7, Win = i < 5 ? match.Win : !match.Win, Items = [3047, 3078, 3053, 0, 0, 0, 3364] })
                { Spells = [4, i % 5 == 1 ? 11 : 14], RoleBoundItem = i % 5 == 1 ? 1103 : null,
                  Runes = new(8000, 8005, 8400, [8005, 9111, 9104, 8014, 8473, 8451]) { PrimarySelections = [8005, 9111, 9104, 8014], SecondarySelections = [8473, 8451] } }).ToArray();
        players = players.Select(p => p with { Stats = p.Stats with { TeamKills = players.Where(t => t.TeamId == p.TeamId).Sum(t => t.Stats.Kills) } }).ToArray();
        return new(match.Id, match.Queue, match.PlayedAt, match.Seconds, players,
            [new(100, match.Win, [157, 238, 11, 35, 122], new Dictionary<string, int> { ["tower"] = 7, ["dragon"] = 3, ["baron"] = 1 }),
             new(200, !match.Win, [64, 103, 86, 51, 89], new Dictionary<string, int> { ["tower"] = 3, ["dragon"] = 1, ["baron"] = 0 })]);
    }
    public static PlayerProfile Create()
    {
        string[] champions = ["Viego", "Vi", "Wukong", "Viego", "Ahri", "Vi", "Jinx", "Wukong", "Viego", "Thresh", "Vi", "Ornn"];
        string[] roles = ["jungle", "jungle", "jungle", "jungle", "middle", "jungle", "bottom", "jungle", "jungle", "utility", "jungle", "top"];
        var matches = champions.Select((name, i) => new ProfileMatch($"DEMO_{i}", name, i % 4 == 0 ? 440 : 420, roles[i], i % 3 != 0, false,
            DateTimeOffset.UtcNow.AddHours(-i * 5 - 1), 1500 + i * 51, 4 + i % 9, 2 + i % 6, 7 + i % 11, 135 + i * 7,
            18000 + i * 1850, 11000 + i * 540, 18 + i * 3, 8 + i, 2 + i % 5, 2 + i % 4, 25 + i) { ChampionId = new[] { 234, 254, 62, 234, 103, 254, 222, 62, 234, 412, 254, 516 }[i], Items = [3047, 3078, 3053, 0, 0, 0, 3364] }).ToArray();
        return new("Invocateur#DEMO", "euw1", 338,
            [new("RANKED_SOLO_5x5", "DIAMOND", "III", 44, 203, 205), new("RANKED_FLEX_SR", "PLATINUM", "I", 84, 146, 134)], matches, DateTimeOffset.UtcNow, true, 12);
    }
}
