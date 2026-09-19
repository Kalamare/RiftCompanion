using System.Net;
using System.Text.Json;
using Rift.Core;

namespace Rift.Infrastructure;

public sealed class RiotApiException(string message) : Exception(message);

public sealed class RiotProfileClient : IDisposable
{
    // Fixed allowlist: never send the developer key to a user-provided host or redirect.
    public static readonly IReadOnlyDictionary<string, (string Label, string Region)> Platforms = new Dictionary<string, (string, string)>
    { ["euw1"] = ("EUW", "europe"), ["eun1"] = ("EUNE", "europe"), ["na1"] = ("NA", "americas"), ["kr"] = ("KR", "asia") };
    private readonly HttpClient http;
    private readonly RiotRequestBudget budget = new();
    private readonly TimeSpan spacing;
    public RiotProfileClient(HttpMessageHandler? handler = null, TimeSpan? requestSpacing = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 8_000_000 };
        spacing = requestSpacing ?? TimeSpan.Zero;
    }
    private async Task<JsonElement?> Get(string host, string endpoint, string key, CancellationToken token)
    {
        if (!Platforms.ContainsKey(host) && !Platforms.Values.Any(p => p.Region == host)) throw new ArgumentException("Région non prise en charge.");
        token.ThrowIfCancellationRequested();
        TimeSpan delay;
        while ((delay = budget.Reserve(host, DateTimeOffset.UtcNow)) > TimeSpan.Zero) await Task.Delay(delay, token);
        if (spacing > TimeSpan.Zero) await Task.Delay(spacing, token);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}.api.riotgames.com{endpoint}");
        request.Headers.Add("X-Riot-Token", key);
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var until = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(2));
            budget.PauseUntil(until);
            throw new RiotApiException($"Quota Riot atteint. Réessaie après {until.ToLocalTime():HH:mm:ss}. Aucun nouvel essai automatique.");
        }
        // Report only a fixed stage label and allowlisted region, never a URL containing player identifiers or the token.
        var stage = endpoint.StartsWith("/riot/account/") ? "compte Riot" :
            endpoint.StartsWith("/lol/summoner/") ? "niveau du joueur" :
            endpoint.StartsWith("/lol/league/") ? "classement" :
            endpoint.Contains("/ids?") ? "liste des parties" : "détail d’une partie";
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new RiotApiException($"Riot HTTP 401 · {stage} · {host}. Authentification non acceptée. Recopie la clé complète depuis le portail développeur Riot.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new RiotApiException($"Riot HTTP 403 · {stage} · {host}. Accès refusé : clé invalide/expirée, autorisation insuffisante ou endpoint non accepté. Ce code ne permet pas de conclure que la clé est expirée.");
        if (!response.IsSuccessStatusCode) throw new RiotApiException($"Riot est indisponible (HTTP {(int)response.StatusCode}). Réessaie plus tard.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); return doc.RootElement.Clone();
    }
    public async Task<PlayerProfile> LoadAsync(string riotId, string platform, string key, ProfileCache cache, IProgress<string>? progress, CancellationToken token, Func<PlayerProfile, Task>? publish = null, int matchCount = 20, PlayerProfile? previous = null)
    {
        if (matchCount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(matchCount), "Choisis entre 1 et 100 parties.");
        var split = riotId.Trim().LastIndexOf('#');
        if (split < 1 || split == riotId.Trim().Length - 1) throw new ArgumentException("Saisis un Riot ID au format Pseudo#TAG.");
        if (!Platforms.TryGetValue(platform, out var routing)) throw new ArgumentException("Choisis un serveur pris en charge.");
        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl)) throw new ArgumentException("Recopie uniquement la clé Riot complète, sans guillemets ni espaces au milieu, dans les paramètres de connexion.");
        riotId = riotId.Trim();
        JsonElement account, summoner;
        string puuid;
        IReadOnlyList<RankEntry> ranks;
        var endTime = previous?.HistoryEndTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var start = previous?.NextStart ?? 0;
        if (previous is not null)
        {
            if (previous.Platform != platform || previous.RiotId != riotId || previous.Puuid.Length == 0 || !previous.HasMore)
                throw new ArgumentException("Cet historique ne peut pas être étendu.");
            puuid = previous.Puuid; ranks = previous.Ranks;
            account = JsonSerializer.SerializeToElement(new { gameName = riotId[..split], tagLine = riotId[(split + 1)..] });
            summoner = JsonSerializer.SerializeToElement(new { summonerLevel = previous.Level, profileIconId = previous.ProfileIconId });
        }
        else
        {
            progress?.Report("Recherche du compte Riot…");
            account = await Get(routing.Region, $"/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(riotId[..split])}/{Uri.EscapeDataString(riotId[(split + 1)..])}", key, token)
                ?? throw new RiotApiException("Riot ID introuvable. Vérifie le pseudo, le tag et le serveur.");
            puuid = DraftParser.Text(account, "puuid"); if (puuid.Length == 0) throw new JsonException();
            summoner = await Get(platform, $"/lol/summoner/v4/summoners/by-puuid/{Uri.EscapeDataString(puuid)}", key, token)
                ?? throw new RiotApiException("Compte LoL introuvable sur ce serveur.");
            var leagues = await Get(platform, $"/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}", key, token);
            var entries = new List<RankEntry>();
            if (leagues is { ValueKind: JsonValueKind.Array }) foreach (var entry in leagues.Value.EnumerateArray())
                entries.Add(new(DraftParser.Text(entry, "queueType"), DraftParser.Text(entry, "tier"), DraftParser.Text(entry, "rank"), DraftParser.Integer(entry, "leaguePoints"), DraftParser.Integer(entry, "wins"), DraftParser.Integer(entry, "losses")));
            ranks = entries;
        }
        string encoded = Uri.EscapeDataString(puuid);
        var ids = await Get(routing.Region, $"/lol/match/v5/matches/by-puuid/{encoded}/ids?start={start}&count={matchCount}&endTime={endTime}", key, token);
        if (ids is not { ValueKind: JsonValueKind.Array }) throw new RiotApiException("Historique temporairement indisponible.");
        await Task.Run(cache.Initialize, token);
        var matches = new List<ProfileMatch>(); int index = 0;
        var consumed = Math.Min(matchCount, ids.Value.GetArrayLength());
        var matchIds = ids.Value.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(matchCount).ToArray();
        PlayerProfile Snapshot(string notice) => new(DraftParser.Text(account, "gameName") + "#" + DraftParser.Text(account, "tagLine"), platform,
            DraftParser.Integer(summoner, "summonerLevel"), ranks, (previous?.Matches ?? []).Concat(matches).DistinctBy(x => x.Id).OrderByDescending(x => x.PlayedAt).ToArray(), DateTimeOffset.UtcNow, false, start + consumed, notice)
        { Puuid = puuid, ProfileIconId = summoner.TryGetProperty("profileIconId", out var icon) && icon.ValueKind == JsonValueKind.Number && icon.TryGetInt32(out var iconId) && iconId >= 0 ? iconId : null,
            NextStart = start + consumed, HistoryEndTime = endTime, HasMore = consumed == matchCount };
        var missing = new List<string>();
        foreach (var id in matchIds)
        {
            var match = await Task.Run(() => cache.Get($"v2:{routing.Region}:{puuid}:{id}"), token);
            if (match is not null) matches.Add(match); else missing.Add(id!);
        }
        index = matches.Count;
        if (publish is not null) await publish(Snapshot("Chargement en cours — statistiques provisoires."));
        // Three outstanding requests maximum. Publish each batch instead of waiting for all requested matches.
        foreach (var batch in missing.Chunk(3))
        {
            token.ThrowIfCancellationRequested();
            using var batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var fetched = await Task.WhenAll(batch.Select(async id =>
            {
                try
                {
                    var raw = await Get(routing.Region, $"/lol/match/v5/matches/{Uri.EscapeDataString(id)}", key, batchCancellation.Token);
                    return raw.HasValue ? ProfileParser.Match(raw.Value, puuid) : null;
                }
                catch { batchCancellation.Cancel(); throw; }
            }));
            for (int i = 0; i < fetched.Length; i++)
                if (fetched[i] is { } match)
                { matches.Add(match); var cacheKey = $"v2:{routing.Region}:{puuid}:{batch[i]}"; await Task.Run(() => cache.Set(cacheKey, match), token); }
            index += batch.Length;
            progress?.Report($"Chargement des parties : {index}/{matchIds.Length}…");
            if (publish is not null) await publish(Snapshot($"Chargement en cours : {index}/{matchIds.Length} — statistiques provisoires."));
        }
        return Snapshot(matches.Count != matchIds.Length ? "Certaines parties sont indisponibles ou exclues (notamment les parties personnalisées)." : "");
    }
    public void Dispose() => http.Dispose();
}
