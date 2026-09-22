using System.Net;
using System.Text.Json;
using Rift.Contracts;
using Rift.Core;

namespace Rift.Backend;

public interface IRiotSource
{
    Task<RiotIdentity> Identity(string platform, string riotId, CancellationToken token);
    Task<string[]> MatchIds(ProfileJob job, CancellationToken token);
    Task<MatchDetails?> Match(string region, string id, CancellationToken token);
}
public sealed class SourceFailure(string code, TimeSpan retryAfter) : Exception(code)
{
    public string Code => Message;
    public TimeSpan RetryAfter => retryAfter;
}

public sealed class RiotSource(BackendOptions options, RiftStore store) : IRiotSource, IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 8_000_000 };
    private async Task<JsonElement?> Get(string host, string path, CancellationToken token)
    {
        if (!ProfileInput.Regions.ContainsKey(host) && !ProfileInput.Regions.Values.Contains(host)) throw new ArgumentException("Route invalide.");
        if (string.IsNullOrWhiteSpace(options.RiotKey)) throw new SourceFailure("riot_key_missing", TimeSpan.FromMinutes(15));
        var delay = await store.ReserveRequest(host, token);
        // Release the collector instead of sleeping while other profiles await their identity.
        if (delay > TimeSpan.Zero) throw new SourceFailure("riot_quota_wait", delay + TimeSpan.FromMilliseconds(50));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}.api.riotgames.com{path}");
        request.Headers.Add("X-Riot-Token", options.RiotKey);
        using var response = await http.SendAsync(request, token);
        foreach (var scope in new[] { "App", "Method" })
        {
            var limits = RateHeader(response, $"X-{scope}-Rate-Limit");
            var counts = RateHeader(response, $"X-{scope}-Rate-Limit-Count");
            foreach (var (seconds, quota) in limits) await store.ObserveLimit(host, seconds, quota, counts.GetValueOrDefault(seconds), token);
        }
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMinutes(2);
            if (retry < TimeSpan.FromSeconds(1)) retry = TimeSpan.FromMinutes(2);
            await store.PauseRequests(host, retry, token); throw new SourceFailure("riot_rate_limited", retry);
        }
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        { await store.PauseRequests(host, TimeSpan.FromMinutes(15), token); throw new SourceFailure("riot_access_denied", TimeSpan.FromMinutes(15)); }
        if (!response.IsSuccessStatusCode) throw new SourceFailure("riot_unavailable", TimeSpan.FromMinutes(2));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); return document.RootElement.Clone();
    }
    public async Task<RiotIdentity> Identity(string platform, string riotId, CancellationToken token)
    {
        var input = ProfileInput.Normalize(new(platform, riotId)); var split = input.RiotId.LastIndexOf('#');
        var account = await Get(ProfileInput.Regions[platform], $"/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(input.RiotId[..split])}/{Uri.EscapeDataString(input.RiotId[(split + 1)..])}", token)
            ?? throw new SourceFailure("player_not_found", TimeSpan.FromHours(6));
        var puuid = DraftParser.Text(account, "puuid"); ProfileInput.Player(platform, puuid);
        var summoner = await Get(platform, $"/lol/summoner/v4/summoners/by-puuid/{Uri.EscapeDataString(puuid)}", token)
            ?? throw new SourceFailure("player_not_found", TimeSpan.FromHours(6));
        var rawRanks = await Get(platform, $"/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}", token);
        if (rawRanks is not { ValueKind: JsonValueKind.Array }) throw new SourceFailure("ranks_unavailable", TimeSpan.FromMinutes(5));
        var ranks = rawRanks.Value.EnumerateArray().Select(e => new RankEntry(DraftParser.Text(e, "queueType"), DraftParser.Text(e, "tier"), DraftParser.Text(e, "rank"),
            DraftParser.Integer(e, "leaguePoints"), DraftParser.Integer(e, "wins"), DraftParser.Integer(e, "losses"))).ToArray();
        return new(puuid, DraftParser.Text(account, "gameName") + "#" + DraftParser.Text(account, "tagLine"), DraftParser.Integer(summoner, "summonerLevel"), DraftParser.Integer(summoner, "profileIconId"), ranks);
    }
    public async Task<string[]> MatchIds(ProfileJob job, CancellationToken token)
    {
        var value = await Get(ProfileInput.Regions[job.Platform], $"/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(job.Puuid!)}/ids?start={job.Cursor}&count={RiftStore.HistoryPageSize}&startTime={job.From.ToUnixTimeSeconds()}&endTime={job.Until.ToUnixTimeSeconds()}", token);
        if (value is not { ValueKind: JsonValueKind.Array } || value.Value.GetArrayLength() > 100) throw new SourceFailure("history_unavailable", TimeSpan.FromMinutes(5));
        return value.Value.EnumerateArray().Select(x => x.GetString() ?? throw new JsonException()).ToArray();
    }
    public async Task<MatchDetails?> Match(string region, string id, CancellationToken token)
    {
        if (id.Length > 100 || !id.Contains('_') || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) throw new JsonException();
        var value = await Get(region, $"/lol/match/v5/matches/{Uri.EscapeDataString(id)}", token);
        return value is null ? null : MatchDetails.Parse(value.Value);
    }
    public void Dispose() => http.Dispose();
    private static Dictionary<int, int> RateHeader(HttpResponseMessage response, string name)
    {
        var result = new Dictionary<int, int>();
        if (response.Headers.TryGetValues(name, out var values))
            foreach (var pair in string.Join(',', values).Split(','))
            {
                var parts = pair.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var count) && int.TryParse(parts[1], out var seconds)) result[seconds] = count;
            }
        return result;
    }
}
