using Rift.Core;
namespace Rift.Desktop;

internal static class ProfileDemo
{
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
