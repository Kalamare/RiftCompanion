using System.Text.Json;
namespace Rift.Infrastructure;
public sealed record RecentProfile(string RiotId, string Platform)
{
    public string Label => $"{RiotId}  ·  {Platform switch { "euw1" => "EUW", "eun1" => "EUNE", "na1" => "NA", "kr" => "KR", _ => Platform }}";
}
public sealed class ProfileHistory(string directory)
{
    private readonly string path = Path.Combine(directory, "recent-profiles.json");
    public IReadOnlyList<RecentProfile> Read()
    {
        if (!File.Exists(path)) return [];
        try { return (JsonSerializer.Deserialize<RecentProfile[]>(File.ReadAllText(path)) ?? []).Where(p => p is not null && !string.IsNullOrWhiteSpace(p.RiotId) && !string.IsNullOrEmpty(p.Platform) && RiotProfileClient.Platforms.ContainsKey(p.Platform)).Take(20).ToArray(); }
        catch (JsonException) { return []; }
    }
    public IReadOnlyList<RecentProfile> Remember(string riotId, string platform)
    {
        var list = new[] { new RecentProfile(riotId, platform) }.Concat(Read().Where(p => !p.RiotId.Equals(riotId,StringComparison.OrdinalIgnoreCase) || p.Platform != platform)).Take(20).ToArray();
        Directory.CreateDirectory(directory); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(list)); File.Move(path + ".tmp",path,true);
        return list;
    }
    public static IReadOnlyList<RecentProfile> Suggest(IEnumerable<RecentProfile> profiles, string query) => profiles.Where(p => p.RiotId.StartsWith(query.Trim(),StringComparison.OrdinalIgnoreCase)).Take(6).ToArray();
}
