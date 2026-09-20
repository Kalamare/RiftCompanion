using System.Net;
using System.Text.Json;
using Rift.Core;
using Rift.Infrastructure;

static class DiagnosticChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var empty = new DiagnosticSnapshot([], [], [], new Dictionary<string, long>());
        var analyzer = new DiagnosticAnalyzer(); var start = DateTimeOffset.UtcNow;
        check(analyzer.Observe(new(start, 90, 100, 90, 0), empty).Count == 0, "analyse : pic ponctuel sans fausse alerte soutenue");
        IReadOnlyList<DiagnosticFinding> findings = [];
        for (int i = 1; i <= 10; i++) findings = analyzer.Observe(new(start.AddSeconds(i), 25, 100, 80, 0), empty);
        check(findings.Count == 2, "analyse : CPU et allocations élevés durablement détectés");
        for (int i = 11; i <= 22; i++) findings = analyzer.Observe(new(start.AddSeconds(i), 1, 100, 1, 0), empty);
        check(findings.Count == 0, "analyse : retour au calme efface les alertes");
        analyzer = new();
        for (int i = 0; i <= 60; i++) findings = analyzer.Observe(new(start.AddSeconds(i), 1, 100 + i * 4, 1, 0), empty);
        check(findings.Any(f => f.Title.Contains("mémoire")), "analyse : tendance mémoire sur une minute, pas un simple pic");
        var quota = empty with { Recent = [new(1, "Riot", "Profil", start, 30, "HTTP 429")] };
        check(new DiagnosticAnalyzer().Observe(new(start, 1, 100, 1, 0), quota).Any(f => f.Title.Contains("Quota")), "analyse : quota HTTP immédiatement signalé");
        check(analyzer.Observe(new(start.AddSeconds(90), 1, 100, 1, 0), empty).Count == 0, "analyse : interruption réinitialise les observations continues");
        var scope = RuntimeDiagnostics.Begin("Test", "Chargement simulé");
        check(RuntimeDiagnostics.Snapshot().Active.Any(x => x.Category == "Test"), "diagnostic : opération en cours visible");
        scope.Dispose(); scope.Dispose();
        Parallel.For(0, 300, _ => { using var op = RuntimeDiagnostics.Begin("Test", "Décodage simulé"); RuntimeDiagnostics.Count("Test · compteur"); });
        var state = RuntimeDiagnostics.Snapshot();
        check(!state.Active.Any(x => x.Category == "Test") && state.Recent.Length == 200 && state.Summaries.Single(x => x.Category == "Test").Completed == 301 && state.Counters["Test · compteur"] == 300,
            "diagnostic : concurrence, fin idempotente et journal borné à 200");
        using var http = new HttpClient(new DiagnosticHttpHandler("Riot", new Handler()));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/lol/league/v4/entries/by-puuid/private-player?secret=value");
        request.Headers.Add("X-Riot-Token", "private-key");
        using var response = await http.SendAsync(request);
        var json = JsonSerializer.Serialize(RuntimeDiagnostics.Snapshot());
        check(!json.Contains("private-player") && !json.Contains("private-key") && !json.Contains("example.invalid") && !json.Contains("secret=value"), "diagnostic : identité, clé et URL absentes du journal");
        check(RuntimeDiagnostics.Snapshot().Recent[0] is { Name: "Classement", Result: "HTTP 429" }, "diagnostic : requête classifiée et HTTP 429 observé");
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
    }
}
