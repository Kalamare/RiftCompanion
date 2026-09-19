using System.Text.Json;

namespace Rift.Core;

public static class Roles
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    { ["top"] = "Top", ["jungle"] = "Jungle", ["middle"] = "Mid", ["bottom"] = "ADC", ["utility"] = "Support" };
    public static string Validate(string? value) => value is not null && Labels.ContainsKey(value) ? value : "jungle";
}

public sealed record Player(int ChampionId, int IntentId, string Role, bool IsYou);
public sealed record Draft(IReadOnlyList<Player> Allies, IReadOnlyList<Player> Enemies,
    IReadOnlyList<int> AllyBans, IReadOnlyList<int> EnemyBans);
public sealed record ClientState(bool Connected, string Phase, Draft? Draft, string Message)
{
    public static ClientState Offline(string message = "Ouvre le client League of Legends.") => new(false, "Offline", null, message);
}

public static class DraftParser
{
    public static Draft? Parse(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("myTeam", out var allies) ||
            !json.TryGetProperty("theirTeam", out var enemies) || allies.ValueKind != JsonValueKind.Array || enemies.ValueKind != JsonValueKind.Array)
            return null;
        int local = Integer(json, "localPlayerCellId", -1);
        Player[] Team(JsonElement rows) => rows.EnumerateArray().Take(5).Select(p =>
        {
            var role = Text(p, "assignedPosition");
            return new Player(Math.Max(0, Integer(p, "championId")), Math.Max(0, Integer(p, "championPickIntent")),
                Roles.Labels.ContainsKey(role) ? role : "", local >= 0 && Integer(p, "cellId", -2) == local);
        }).ToArray();
        var bans = json.TryGetProperty("bans", out var b) ? b : default;
        int[] Bans(string key) => bans.ValueKind == JsonValueKind.Object && bans.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var id) && id > 0).Select(x => x.GetInt32()).ToArray() : [];
        return new Draft(Team(allies), Team(enemies), Bans("myTeamBans"), Bans("theirTeamBans"));
    }
    public static int Integer(JsonElement obj, string key, int fallback = 0) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    public static string Text(JsonElement obj, string key) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
