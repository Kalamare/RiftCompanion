using System.Text.Json;
using Rift.Core;
using Rift.Contracts;

namespace Rift.Backend;

public sealed partial class RiftStore
{
    public async Task<PlayerProfile?> Profile(string platform, string puuid, int start, int count, long end, CancellationToken token)
    {
        if (start < 0 || start > 10000 || count is < 1 or > 50 || end < 0 || end > DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds())
            throw new ArgumentException("Page invalide.");
        var player = await Player(platform, puuid, token); if (player is null) return null;
        if (end == 0) end = DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeSeconds();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var command = Command(connection, """
            SELECT m.details::text FROM contributions c JOIN matches m USING(region,match_id)
            WHERE c.platform=$1 AND c.puuid=$2 AND m.played_at < $3
            ORDER BY m.played_at DESC,m.match_id DESC OFFSET $4 LIMIT $5
            """, platform, puuid, DateTimeOffset.FromUnixTimeSeconds(end), start, count + 1);
        await using var reader = await command.ExecuteReaderAsync(token); var rows = new List<ProfileMatch>();
        while (await reader.ReadAsync(token)) rows.Add(JsonSerializer.Deserialize<MatchDetails>(reader.GetString(0))!.Participants.Single(p => p.Puuid == puuid).Stats);
        return new(player.RiotId, platform, player.Level ?? 0, player.Ranks, rows.Take(count).ToArray(), DateTimeOffset.UtcNow, false, count,
            !HistoryBackfillEnabled ? "Collecte limitée aux 20 dernières parties · statistiques sur les parties affichées." :
            player.Coverage.AccessibleHistoryScanned ? "Historique accessible parcouru par le serveur." : "Historique partiel · mise à jour serveur en cours.")
        { Puuid = puuid, ProfileIconId = player.ProfileIconId, NextStart = start + Math.Min(rows.Count, count), HasMore = rows.Count > count, HistoryEndTime = end };
    }
    public async Task<MatchDetails?> Details(string platform, string id, CancellationToken token)
    {
        if (!ProfileInput.Regions.TryGetValue(platform, out var region) || !id.StartsWith(platform + "_", StringComparison.OrdinalIgnoreCase) || id.Length > 100)
            throw new ArgumentException("Partie invalide.");
        await using var connection = await source.OpenConnectionAsync(token);
        await using var command = Command(connection, "SELECT details::text FROM matches WHERE region=$1 AND match_id=$2", region, id);
        return await command.ExecuteScalarAsync(token) is string json ? JsonSerializer.Deserialize<MatchDetails>(json) : null;
    }
}
