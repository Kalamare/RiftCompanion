using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Rift.Core;
using Rift.Infrastructure;

static class PerformanceChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var budget = new RiotRequestBudget();
        check(Enumerable.Range(0, 18).All(_ => budget.Reserve("europe", now) == TimeSpan.Zero) && budget.Reserve("europe", now) >= TimeSpan.FromSeconds(1), "performance : rafale bornée à 18 appels par seconde");
        check(budget.Reserve("euw1", now) == TimeSpan.Zero, "performance : budgets séparés par région");
        for (int s = 1; s < 5; s++) for (int i = 0; i < 18; i++) budget.Reserve("europe", now.AddSeconds(s));
        check(budget.Reserve("europe", now.AddSeconds(5)) == TimeSpan.FromSeconds(115), "performance : limite glissante de 90 appels sur deux minutes");
        budget.PauseUntil(now.AddMinutes(3));
        check(budget.Reserve("euw1", now) == TimeSpan.FromMinutes(3), "performance : Retry-After suspend toutes les nouvelles réservations");
        var folder = Path.Combine(Path.GetTempPath(), "RiftPerf-" + Guid.NewGuid().ToString("N"));
        try
        {
            int active = 0, maximum = 0, details = 0;
            using var client = new RiotProfileClient(new Handler(async (request, ct) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                string body;
                if (path.Contains("/account/")) body = "{\"puuid\":\"p\",\"gameName\":\"Test\",\"tagLine\":\"EUW\"}";
                else if (path.Contains("/summoner/")) body = "{\"summonerLevel\":42}";
                else if (path.Contains("/league/")) body = "[]";
                else if (path.EndsWith("/ids")) body = JsonSerializer.Serialize(Enumerable.Range(0, 20).Select(i => $"EUW1_{i}"));
                else
                {
                    Interlocked.Increment(ref details); var count = Interlocked.Increment(ref active);
                    lock (folder) maximum = Math.Max(maximum, count);
                    try { await Task.Delay(80, ct); }
                    finally { Interlocked.Decrement(ref active); }
                    body = JsonSerializer.Serialize(new { metadata = new { matchId = path.Split('/').Last() }, info = new { queueId = 420, mapId = 11, gameDuration = 1200, participants = new[] { new { puuid = "p", championName = "Vi", championId = 254 } } } });
                }
                return new(HttpStatusCode.OK) { Content = new StringContent(body) };
            }));
            var cache = new ProfileCache(Path.Combine(folder, "cache.db"));
            var counts = new List<int>(); var clock = Stopwatch.StartNew(); double first = -1;
            var result = await client.LoadAsync("Test#EUW", "euw1", "test", cache, null, default, snapshot =>
            { counts.Add(snapshot.Matches.Count); if (snapshot.Matches.Count > 0 && first < 0) first = clock.Elapsed.TotalMilliseconds; return Task.CompletedTask; });
            var cold = clock.Elapsed.TotalMilliseconds;
            check(maximum == 3 && result.Matches.Count == 20 && result.Matches.Select(m => m.Id).Distinct().Count() == 20, "performance : trois matchs simultanés maximum, aucun doublon/perte");
            check(counts.Contains(3) && counts.Last() == 20 && first < cold, "performance : premiers matchs publiés avant la fin du chargement");
            clock.Restart(); counts.Clear();
            await client.LoadAsync("Test#EUW", "euw1", "test", cache, null, default, snapshot => { counts.Add(snapshot.Matches.Count); return Task.CompletedTask; });
            check(details == 20 && counts.First() == 20, "performance : historique en cache publié en une fois sans recharger les détails");
            Console.WriteLine($"MESURE simulation 80 ms/match : premier lot {first:F0} ms ; 20 matchs {cold:F0} ms ; cache {clock.Elapsed.TotalMilliseconds:F0} ms (hors réseau Riot réel).");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => response(request, token);
    }
}
