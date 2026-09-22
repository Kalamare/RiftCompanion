using Rift.Core;

namespace Rift.Contracts;

public sealed record ProfileLookup(string Platform, string RiotId);
public sealed record LookupReceipt(Guid Id);
public sealed record RefreshStatus(string Status, DateTimeOffset ServerTime, DateTimeOffset? LastUpdatedAt,
    DateTimeOffset? NextAllowedAt, bool Accepted = false);
public sealed record HistoryCoverage(DateTimeOffset? From, DateTimeOffset? Until, bool AccessibleHistoryScanned,
    int MissingDetails, string Status);
public sealed record PlayerCard(string Platform, string Puuid, string RiotId, int? Level, int? ProfileIconId,
    RankEntry[] Ranks, DateTimeOffset? RanksUpdatedAt, DateTimeOffset LastSeen, long Version, HistoryCoverage Coverage);
public sealed record LookupStatus(Guid Id, string Status, string? Error, PlayerCard? Player);
public sealed record ChampionStats(int ChampionId, string Champion, ProfileSummary Summary);
public sealed record RoleStats(string Role, ProfileSummary Summary);
public sealed record StatsSnapshot(string Platform, string Puuid, DateTimeOffset From, DateTimeOffset Until,
    int Queue, ProfileSummary Summary, ChampionStats[] Champions, RoleStats[] Roles, long Version, HistoryCoverage Coverage);
public sealed record ProfileChanged(long Sequence, string Platform, string Puuid, long Version);

public static class ProfileInput
{
    public static readonly IReadOnlyDictionary<string, string> Regions = new Dictionary<string, string>
    { ["euw1"] = "europe", ["eun1"] = "europe", ["na1"] = "americas", ["kr"] = "asia" };
    public static ProfileLookup Normalize(ProfileLookup input)
    {
        var platform = input.Platform?.Trim().ToLowerInvariant() ?? "";
        var id = input.RiotId?.Trim() ?? ""; var split = id.LastIndexOf('#');
        if (!Regions.ContainsKey(platform) || id.Length > 100 || split < 1 || split == id.Length - 1 || id.Any(char.IsControl))
            throw new ArgumentException("Serveur ou Riot ID invalide.");
        return new(platform, id);
    }
    public static void Player(string platform, string puuid)
    { if (!Regions.ContainsKey(platform) || string.IsNullOrWhiteSpace(puuid) || puuid.Length > 256 || puuid.Any(char.IsControl)) throw new ArgumentException("Joueur invalide."); }
}
