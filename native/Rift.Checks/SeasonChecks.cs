using System.Net;
using System.Text.Json;
using Rift.Core;
using Rift.Infrastructure;

static class SeasonChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-60);
        var match = new ProfileMatch("EUW1_1", "Ornn", 420, "top", true, false, from.AddDays(1), 600, 2, 1, 4, 60, 1000, 5000, 10, 2, 1, 1, 10) { ChampionId = 516 };
        var other = match with { Id = "EUW1_2", Queue = 450, Seconds = 1800, Cs = 300, Kills = 4, Deaths = 3, Assists = 2, TeamKills = 0 };
        var history = new SeasonHistory("euw1", "season-player", from, DateTimeOffset.UtcNow, 0, false, 0,
            [match, match, other, match with { Id = "remake", Remake = true }, match with { Id = "old", PlayedAt = from.AddDays(-1) }]);
        check(history.Select(0).Length == 2 && history.Select(420).Single().Id == match.Id && history.Select(-1).Length == 1 && history.Select(440).Length == 0,
            "Saison : période, modes indépendants, remakes exclus et IDs dédupliqués");
        var summary = ProfileSummary.From(history.Select(0));
        check(summary.Kda == 3 && summary.CsPerMinute == 9 && summary.Participation == 60 && summary.Kills == 3,
            "Saison : KDA cumulé, CS/min pondérés par durée, participation sans division par zéro, K/D/A moyens");
        var directory = Path.Combine(Path.GetTempPath(), "RiftSeasonChecks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SeasonHistoryStore(directory); var cache = new ProfileCache(Path.Combine(directory, "matches.db")); cache.Initialize();
            for (int i = 0; i < 100; i++) cache.Set($"v2:europe:season-player:EUW1_{i}", match with { Id = $"EUW1_{i}" });
            int lists = 0, details = 0; string? frozenEnd = null;
            using var api = new RiotProfileClient(new Handler(request =>
            {
                check(request.RequestUri!.Host == "europe.api.riotgames.com" && request.Headers.GetValues("X-Riot-Token").Single() == "test-key", "Saison : hôte Riot fixe et authentification dédiée");
                if (request.RequestUri.AbsolutePath.EndsWith("/ids"))
                {
                    lists++; var query = request.RequestUri.Query;
                    check(query.Contains("count=100") && query.Contains($"startTime={from.ToUnixTimeSeconds()}"), "Saison : pagination bornée et début de période transmis");
                    string end = query.Split("endTime=")[1]; frozenEnd ??= end;
                    check(frozenEnd == end, "Saison : borne de fin stable entre les pages");
                    return Json(query.Contains("start=0&") ? Enumerable.Range(0, 100).Select(i => $"EUW1_{i}").ToArray() : new[] { "EUW1_missing" });
                }
                details++; return new(HttpStatusCode.NotFound);
            }));
            var loaded = await api.LoadSeasonAsync("euw1", "season-player", "test-key", from, cache, store, null, default);
            check(loaded.Exhausted && loaded.NextStart == 101 && loaded.Matches.Length == 100 && loaded.Missing == 1 && lists == 2 && details == 1,
                "Saison : pages parcourues, détails déjà connus réutilisés, absence signalée sans faux zéro");
            check(store.Read("euw1", "other-player", from) is null && store.Read("euw1", "season-player", from)?.Matches.Length == 100,
                "Saison : cache persistant isolé par compte et période");
            store.Save(loaded with { Exhausted = false, NextStart = 100, Missing = 0 });
            lists = details = 0;
            var resumed = await api.LoadSeasonAsync("euw1", "season-player", "test-key", from, cache, store, null, default);
            check(lists == 1 && resumed.Until == loaded.Until && resumed.Missing == 1, "Saison : reprise au dernier point enregistré sans déplacer la période");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); int before = lists;
            bool stopped = false;
            try { await api.LoadSeasonAsync("euw1", "season-player", "test-key", from, cache, store, null, cancelled.Token); }
            catch (OperationCanceledException) { stopped = true; }
            check(stopped && before == lists, "Saison : annulation sans nouvelle requête");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request)); }
}
