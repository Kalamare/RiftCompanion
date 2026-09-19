namespace Rift.Core;

public static class ProfileLinks
{
    public static Uri? LeagueOfGraphs(string riotId, string platform)
    {
        var region = platform switch { "euw1" => "euw", "eun1" => "eune", "na1" => "na", "kr" => "kr", _ => null };
        var separator = riotId.LastIndexOf('#');
        if (region is null || separator <= 0 || separator == riotId.Length - 1) return null;
        var name = Uri.EscapeDataString(riotId[..separator]);
        var tag = Uri.EscapeDataString(riotId[(separator + 1)..]);
        return new Uri($"https://www.leagueofgraphs.com/fr/summoner/{region}/{name}-{tag}");
    }
}
