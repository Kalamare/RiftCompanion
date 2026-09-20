using System.Net;
using Rift.Core;
using Rift.Infrastructure;

static class PlayerRankChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "RiftRanks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ProfileCache(Path.Combine(folder, "cache.db")); cache.Initialize();
            int calls = 0;
            using var api = new RiotProfileClient(new Handler(request =>
            {
                calls++;
                if (request.RequestUri!.Host != "euw1.api.riotgames.com" || !request.RequestUri.AbsolutePath.EndsWith("/entries/by-puuid/player")) throw new Exception("Wrong rank route");
                return new(HttpStatusCode.OK) { Content = new StringContent("""[{"queueType":"RANKED_FLEX_SR","tier":"EMERALD","rank":"II","leaguePoints":31,"wins":12,"losses":8}]""") };
            }));
            var first = await api.LoadPlayerRanksAsync("player", "euw1", "test-key", cache, default);
            var second = await api.LoadPlayerRanksAsync("player", "euw1", "", cache, default);
            check(calls == 1 && first?.Entries.Single().Tier == "EMERALD" && second?.FetchedAt == first.FetchedAt, "rangs : classement par PUUID, cache daté réutilisé sans clé");
            check(await api.LoadPlayerRanksAsync("other", "euw1", "", cache, default) is null && cache.GetRanks("na1", "player") is null, "rangs : absent distinct de non classé et serveurs isolés");
            cache.SetRanks("euw1", "player", first! with { FetchedAt = DateTimeOffset.UtcNow.AddHours(-2) });
            await api.LoadPlayerRanksAsync("player", "euw1", "test-key", cache, default);
            check(calls == 2, "rangs : cache actualisé après une heure");
            using var empty = new RiotProfileClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("[]") }));
            check((await empty.LoadPlayerRanksAsync("unranked", "euw1", "test-key", cache, default))?.Entries.Count == 0, "rangs : réponse vide confirmée = non classé");
            using var failed = new RiotProfileClient(new Handler(_ => new(HttpStatusCode.Forbidden)));
            cache.SetRanks("euw1", "player", first! with { FetchedAt = DateTimeOffset.UtcNow.AddHours(-2) });
            var stale = await failed.LoadPlayerRanksAsync("player", "euw1", "test-key", cache, default);
            check(stale?.Entries.Count == 1 && stale.FetchedAt < DateTimeOffset.UtcNow.AddHours(-1), "rangs : ancien résultat daté conservé si Riot refuse l’actualisation");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
