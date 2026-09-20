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
        http = new HttpClient(new DiagnosticHttpHandler("Riot", handler ?? new HttpClientHandler { AllowAutoRedirect = false })) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 8_000_000 };
        spacing = requestSpacing ?? TimeSpan.Zero;
    }
    private async Task<JsonElement?> Get(string host, string endpoint, string key, CancellationToken token)
    {
        if (!Platforms.ContainsKey(host) && !Platforms.Values.Any(p => p.Region == host)) throw new ArgumentException("Région non prise en charge.");
        token.ThrowIfCancellationRequested();
        TimeSpan delay;
        while ((delay = budget.Reserve(host, DateTimeOffset.UtcNow)) > TimeSpan.Zero) { using var waiting = RuntimeDiagnostics.Begin("Quotas", "Attente du budget Riot"); await Task.Delay(delay, token); }
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
    public async Task<PlayerProfile> LoadAsync(string riotId, string platform, string key, ProfileCache cache, IProgress<string>? progress, CancellationToken token, Func<PlayerProfile, Task>? publish = null, int matchCount = 20, PlayerProfile? previous = null, string? targetPuuid = null)
    {
        if (matchCount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(matchCount), "Choisis entre 1 et 100 parties.");
        var split = riotId.Trim().LastIndexOf('#');
        if (targetPuuid is null && (split < 1 || split == riotId.Trim().Length - 1)) throw new ArgumentException("Saisis un Riot ID au format Pseudo#TAG.");
        if (targetPuuid is not null && (string.IsNullOrWhiteSpace(targetPuuid) || targetPuuid.Length > 256 || previous is not null)) throw new ArgumentException("Identité du joueur invalide.");
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
            var accountPath = targetPuuid is not null ? $"/riot/account/v1/accounts/by-puuid/{Uri.EscapeDataString(targetPuuid)}"
                : $"/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(riotId[..split])}/{Uri.EscapeDataString(riotId[(split + 1)..])}";
            account = await Get(routing.Region, accountPath, key, token)
                ?? throw new RiotApiException("Riot ID introuvable. Vérifie le pseudo, le tag et le serveur.");
            puuid = DraftParser.Text(account, "puuid"); if (puuid.Length == 0) throw new JsonException();
            if (targetPuuid is not null && puuid != targetPuuid) throw new RiotApiException("L’identité renvoyée par Riot ne correspond pas au joueur sélectionné.");
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
                    return (Match: raw.HasValue ? ProfileParser.Match(raw.Value, puuid) : null,
                        Details: raw.HasValue ? MatchDetails.Parse(raw.Value) : null);
                }
                catch { batchCancellation.Cancel(); throw; }
            }));
            for (int i = 0; i < fetched.Length; i++)
                if (fetched[i].Match is { } match)
                {
                    matches.Add(match); var cacheKey = $"v2:{routing.Region}:{puuid}:{batch[i]}";
                    var details = fetched[i].Details;
                    // Serialize SQLite writes after the network batch to avoid writer contention.
                    await Task.Run(() => {
                        cache.Set(cacheKey, match);
                        if (details is not null && details.Id == match.Id) cache.SetDetails(routing.Region, details);
                    }, token);
                }
            index += batch.Length;
            progress?.Report($"Chargement des parties : {index}/{matchIds.Length}…");
            if (publish is not null) await publish(Snapshot($"Chargement en cours : {index}/{matchIds.Length} — statistiques provisoires."));
        }
        return Snapshot(matches.Count != matchIds.Length ? "Certaines parties sont indisponibles ou exclues (notamment les parties personnalisées)." : "");
    }
    public async Task<MatchDetails> LoadDetailsAsync(string id, string platform, string key, ProfileCache cache, CancellationToken token)
    {
        if (!Platforms.TryGetValue(platform, out var routing)) throw new ArgumentException("Serveur non pris en charge.");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Identifiant de partie invalide.");
        await Task.Run(cache.Initialize, token);
        var saved = await Task.Run(() => cache.GetDetails(routing.Region, id), token);
        token.ThrowIfCancellationRequested();
        if (saved is not null && saved.Id == id && (saved.SchemaVersion >= 4 || string.IsNullOrWhiteSpace(key))) return saved;
        key = key.Trim();
        if (key.Length == 0 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
            throw new RiotApiException("Ajoute une clé Riot dans Connexion aux données Riot pour ouvrir cette partie non enregistrée.");
        JsonElement? raw;
        try { raw = await Get(routing.Region, $"/lol/match/v5/matches/{Uri.EscapeDataString(id)}", key, token); }
        catch (Exception ex) when (saved is not null && (ex is RiotApiException or HttpRequestException || ex is OperationCanceledException && !token.IsCancellationRequested)) { return saved; }
        var details = raw.HasValue ? MatchDetails.Parse(raw.Value) : null;
        if (details is null || details.Id != id) throw new RiotApiException("Le détail de cette partie est indisponible auprès de Riot.");
        await Task.Run(() => cache.SetDetails(routing.Region, details), token);
        return details;
    }
    public void Dispose() => http.Dispose();
    public async Task<PlayerRanks?> LoadPlayerRanksAsync(string puuid, string platform, string key, ProfileCache cache, CancellationToken token)
    {
        if (!Platforms.ContainsKey(platform) || string.IsNullOrWhiteSpace(puuid)) throw new ArgumentException("Joueur ou serveur invalide.");
        var saved = await Task.Run(() => cache.GetRanks(platform, puuid), token);
        if (saved is not null && DateTimeOffset.UtcNow - saved.FetchedAt < TimeSpan.FromHours(1)) return saved;
        if (string.IsNullOrWhiteSpace(key)) return saved;
        try
        {
            var raw = await Get(platform, $"/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}", key.Trim(), token);
            if (raw is not { ValueKind: JsonValueKind.Array }) return saved;
            var ranks = new PlayerRanks(raw.Value.EnumerateArray().Select(e => new RankEntry(DraftParser.Text(e, "queueType"), DraftParser.Text(e, "tier"),
                DraftParser.Text(e, "rank"), DraftParser.Integer(e, "leaguePoints"), DraftParser.Integer(e, "wins"), DraftParser.Integer(e, "losses"))).ToArray(), DateTimeOffset.UtcNow);
            await Task.Run(() => cache.SetRanks(platform, puuid, ranks), token);
            return ranks;
        }
        catch (Exception ex) when (saved is not null && (ex is RiotApiException or HttpRequestException || ex is OperationCanceledException && !token.IsCancellationRequested)) { return saved; }
    }
}
