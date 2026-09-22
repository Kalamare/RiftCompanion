using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Rift.Contracts;
using Rift.Core;

namespace Rift.Client;

public sealed class ServerWorkLimitException(TimeSpan wait) : HttpRequestException("Budget de nouvelles recherches atteint.")
{
    public TimeSpan Wait { get; } = wait;
}

public sealed class RiftServerClient : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly HubConnection hub;
    private readonly SemaphoreSlim subscription = new(1, 1);
    public event Action? Changed;
    private (string Platform, string Puuid)? watched;
    public static RiftServerClient? FromEnvironment()
    {
        var url = Environment.GetEnvironmentVariable("RIFT_SERVER_URL");
        if (string.IsNullOrWhiteSpace(url)) return null;
        var path = Environment.GetEnvironmentVariable("RIFT_SERVER_ACCESS_KEY_FILE");
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("RIFT_SERVER_ACCESS_KEY_FILE est requis pour le serveur privé de développement.");
        return new(new Uri(url), File.ReadAllText(path).Trim());
    }
    public RiftServerClient(Uri url, string key)
    {
        if (!url.IsAbsoluteUri || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 ||
            (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback)) || key.Length is < 32 or > 256)
            throw new ArgumentException("Serveur HTTPS requis (HTTP autorisé uniquement en local).");
        var address = new Uri(url.AbsoluteUri.TrimEnd('/') + "/");
        http = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 8_000_000 };
        http.DefaultRequestHeaders.Add("X-Rift-Key", key);
        hub = new HubConnectionBuilder().WithUrl(new Uri(address, "v1/events"), o =>
        {
            o.Headers["X-Rift-Key"] = key;
            o.HttpMessageHandlerFactory = handler => { if (handler is HttpClientHandler client) client.AllowAutoRedirect = false; return handler; };
        }).WithAutomaticReconnect().Build();
        hub.On<ProfileChanged>("ProfileChanged", _ => Changed?.Invoke());
        hub.Reconnected += async _ =>
        {
            if (watched is { } w) await hub.InvokeAsync("Watch", w.Platform, w.Puuid);
            Changed?.Invoke(); // Reconcile REST state after any gap in notifications.
        };
    }
    private static string Segment(string value) => Uri.EscapeDataString(value);
    private static string PlayerPath(string platform, string puuid) => $"v1/players/{Segment(platform)}/{Segment(puuid)}";
    private async Task<T> Get<T>(string path, CancellationToken token) =>
        await http.GetFromJsonAsync<T>(path, token) ?? throw new HttpRequestException("Réponse serveur vide.");
    public async Task<PlayerProfile> Load(string riotId, string platform, CancellationToken token, PlayerProfile? previous = null, string? expectedPuuid = null)
    {
        string puuid;
        if (previous is not null) puuid = previous.Puuid;
        else
        {
            using var response = await http.PostAsJsonAsync("v1/profiles/lookup", new ProfileLookup(platform, riotId), token);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw new ServerWorkLimitException(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            response.EnsureSuccessStatusCode();
            var receipt = (await response.Content.ReadFromJsonAsync<LookupReceipt>(token))!;
            PlayerCard? player = null;
            for (int i = 0; i < 45; i++)
            {
                var status = await Get<LookupStatus>("v1/lookups/" + receipt.Id, token);
                if (status.Player is not null) { player = status.Player; break; }
                if (status.Status == "paused" && status.Error != "riot_quota_wait") throw new HttpRequestException("Collecte serveur différée : " + status.Error);
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
            puuid = player?.Puuid ?? throw new HttpRequestException("Le serveur prépare encore ce profil. Réessaie dans quelques instants.");
        }
        if (expectedPuuid is not null && puuid != expectedPuuid) throw new HttpRequestException("Le Riot ID a changé ; le profil demandé ne correspond plus à ce nom.");
        var result = await Profile(platform, puuid, previous?.NextStart ?? 0, previous?.HistoryEndTime ?? 0, token);
        return previous is null ? result : result with { Matches = previous.Matches.Concat(result.Matches).DistinctBy(m => m.Id).ToArray() };
    }
    public Task<PlayerProfile> Profile(string platform, string puuid, int start, long end, CancellationToken token) =>
        Get<PlayerProfile>(PlayerPath(platform, puuid) + $"/profile?start={start}&count=20&end={end}", token);
    public Task<PlayerCard> Card(string platform, string puuid, CancellationToken token) => Get<PlayerCard>(PlayerPath(platform, puuid), token);
    public async Task<RefreshStatus> Refresh(string platform, string puuid, bool request, CancellationToken token)
    {
        var path = PlayerPath(platform, puuid) + "/refresh";
        if (!request) return await Get<RefreshStatus>(path, token);
        using var response = await http.PostAsync(path, null, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RefreshStatus>(token) ?? throw new HttpRequestException("État d’actualisation absent.");
    }
    public Task<StatsSnapshot> Stats(string platform, string puuid, DateTimeOffset from, int queue, CancellationToken token) =>
        Get<StatsSnapshot>(PlayerPath(platform, puuid) + $"/stats?from={from:yyyy-MM-dd}T00:00:00Z&until={DateTime.UtcNow.AddDays(1):yyyy-MM-dd}T00:00:00Z&queue={queue}", token);
    public Task<MatchDetails> Details(string platform, string id, CancellationToken token) => Get<MatchDetails>($"v1/matches/{Segment(platform)}/{Segment(id)}", token);
    public async Task<PlayerRanks?> Ranks(string platform, string puuid, CancellationToken token)
    {
        var card = await Get<PlayerCard>(PlayerPath(platform, puuid), token);
        return card.RanksUpdatedAt is { } date ? new(card.Ranks, date) : null;
    }
    public async Task Watch(string platform, string puuid, CancellationToken token)
    {
        await subscription.WaitAsync(token);
        try
        {
            watched = (platform, puuid);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            if (hub.State == HubConnectionState.Disconnected) await hub.StartAsync(deadline.Token);
            if (hub.State == HubConnectionState.Connected) await hub.InvokeAsync("Watch", platform, puuid, deadline.Token);
        }
        finally { subscription.Release(); }
    }
    public async ValueTask DisposeAsync() { await hub.DisposeAsync(); http.Dispose(); subscription.Dispose(); }
}
