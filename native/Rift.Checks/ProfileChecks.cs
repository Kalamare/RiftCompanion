using System.Net;
using System.Text.Json;
using Rift.Core;
using Rift.Infrastructure;

static class ProfileChecks
{
    private const string MatchJson = """
    {"metadata":{"matchId":"EUW1_123"},"info":{"queueId":420,"mapId":11,"gameCreation":1760000000000,"gameDuration":1200,"participants":[
    {"puuid":"selected","teamId":100,"championName":"Vi","teamPosition":"JUNGLE","win":true,"kills":5,"deaths":2,"assists":5,"totalMinionsKilled":20,"neutralMinionsKilled":100,"totalDamageDealtToChampions":10000,"goldEarned":8000,"visionScore":18},
    {"puuid":"teammate","teamId":100,"kills":15},{"puuid":"opponent","teamId":200,"kills":30}]}}
    """;
    private static JsonElement Json(string value) { using var doc = JsonDocument.Parse(value); return doc.RootElement.Clone(); }
    public static async Task Run(Action<bool, string> check)
    {
        var match = ProfileParser.Match(Json(MatchJson), "selected")!;
        check(match.Cs == 120 && match.Role == "jungle" && match.TeamKills == 20 && match.Participation == 50, "profil : jungle, CS et participation de la bonne équipe");
        check(ProfileParser.Match(Json(MatchJson), "absent") is null && ProfileParser.Match(Json("null"), "selected") is null, "profil : joueur absent et document invalide exclus");
        check(ProfileParser.Match(Json(MatchJson.Replace("\"queueId\":420", "\"queueId\":0")), "selected") is null, "profil : partie personnalisée exclue");
        check(ProfileParser.Match(Json(MatchJson.Replace("\"mapId\":11", "\"mapId\":12")), "selected")!.Role == "", "profil : pas de rôle inventé en ARAM");
        var summary = ProfileSummary.From([match, match with { Seconds = 2400, Cs = 300, Deaths = 4, Win = false }, match with { Remake = true, Cs = 9999 }]);
        check(summary.Games == 2 && summary.WinRate == 50 && summary.CsPerMinute == 7 && Math.Abs(summary.Kda - 20.0 / 6) < .001, "profil : durée pondérée, KDA global et exclusion des remakes");
        check(ProfileSummary.From([]).Games == 0 && ProfileSummary.From([match with { TeamKills = 0 }]).Participation is null, "profil : échantillon vide et participation inconnue");
        check(QueueCatalog.Label(710) == "Classé 5c5" && QueueCatalog.Label(400) == "Mode Draft" && QueueCatalog.Label(870).StartsWith("Coop vs IA"), "files : classée 5v5, normale et coop identifiées");
        check(QueueCatalog.Label(999999).StartsWith("Mode non répertorié"), "files : type inconnu non inventé");
        check(QueueCatalog.Label(1740) == "Arena courage" && QueueCatalog.Label(1750) == "Arena 3x6", "files : variantes Arena issues du catalogue français du client");
        check(!QueueCatalog.HasStandardRoles(900) && !QueueCatalog.HasStandardRoles(1900) && !QueueCatalog.HasStandardRoles(1740) && !QueueCatalog.HasStandardRoles(450) && QueueCatalog.HasStandardRoles(420), "rôles : ARURF, URF, Arena et ARAM exclus même avec un rôle en cache");
        check(ProfileParser.Match(Json(MatchJson.Replace("\"queueId\":420", "\"queueId\":900")), "selected")!.Role == "", "rôles : aucun rôle classique attribué en ARURF sur la Faille");
        var parsedQueues = QueueCatalog.ParseClient(Json("""[{"id":9999,"name":"Nouveau mode","gameSelectModeGroup":"kAlternativeLeagueGameModes"}]"""));
        check(parsedQueues[9999].Name == "Nouveau mode", "files : nouvel identifiant parsé sans ajout de switch");
        var fallback = ProfileParser.Match(Json(MatchJson.Replace("\"teamPosition\":\"JUNGLE\"", "\"teamPosition\":\"\",\"individualPosition\":\"JUNGLE\"")), "selected")!;
        check(fallback.Role == "jungle" && (match with { Role = "" }).RoleLabel == "Rôle non fourni par Riot" && (match with { Role = "", Queue = 450 }).RoleLabel == "Sans rôle standard", "rôles : secours Riot et distinction absent / non applicable");
        var folder = Path.Combine(Path.GetTempPath(), "RiftProfileChecks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ProfileCache(Path.Combine(folder, "cache.db")); cache.Initialize(); cache.Set("test", match);
            using (var source = typeof(QueueCatalog).Assembly.GetManifestResourceStream("Rift.Core.Data.client-queues-fr.json")!)
            using (var reader = new StreamReader(source))
            {
                var json = await reader.ReadToEndAsync(); int catalogCalls = 0; bool publicRequest = false;
                using (var updater = new QueueCatalogUpdater(folder, new Handler(request =>
                {
                    catalogCalls++; publicRequest = !request.Headers.Contains("X-Riot-Token") && request.RequestUri!.Host == "raw.communitydragon.org";
                    return new(HttpStatusCode.OK) { Content = new StringContent(json) };
                })))
                { await updater.PrepareAsync(default); await updater.PrepareAsync(default); }
                using (var cachedUpdater = new QueueCatalogUpdater(folder, new Handler(_ => throw new Exception("Fresh catalog should not use network"))))
                    await cachedUpdater.PrepareAsync(default);
                check(catalogCalls == 1 && publicRequest && QueueCatalog.Label(1750) == "Arena 3x6", "catalogue : cache quotidien, réouverture sans réseau et aucune clé transmise");
                File.SetLastWriteTimeUtc(Path.Combine(folder, "client-queues-fr.json"), DateTime.UtcNow.AddDays(-2));
                using (var offline = new QueueCatalogUpdater(folder, new Handler(_ => throw new HttpRequestException("offline"))))
                    await offline.PrepareAsync(default);
                check(QueueCatalog.Label(1740) == "Arena courage", "catalogue : mode hors ligne conserve les noms connus");
            }
            var cached = new ProfileCache(Path.Combine(folder, "cache.db")).Get("test");
            check(cached is not null && cached.Id == match.Id && cached.Cs == match.Cs && cached.Items.SequenceEqual(match.Items) && cache.Get("other-player") is null, "profil : cache SQLite persistant et isolé par clé");
            var requests = new List<string>(); bool credentialsOk = true;
            using var client = new RiotProfileClient(new Handler(request =>
            {
                requests.Add(request.RequestUri!.AbsoluteUri);
                credentialsOk &= request.Headers.GetValues("X-Riot-Token").Single() == "test-key" && request.RequestUri.Scheme == "https";
                var path = request.RequestUri.AbsolutePath;
                string body = path.Contains("/riot/account/") ? "{\"puuid\":\"selected\",\"gameName\":\"Test\",\"tagLine\":\"EUW\"}" :
                    path.Contains("/summoner/") ? "{\"summonerLevel\":42}" : path.Contains("/league/") ? "[]" : path.EndsWith("/ids") ? "[\"EUW1_123\"]" : MatchJson;
                return new(HttpStatusCode.OK) { Content = new StringContent(body) };
            }), TimeSpan.Zero);
            var loaded = await client.LoadAsync("Test#EUW", "euw1", "test-key", cache, null, default);
            await client.LoadAsync("Test#EUW", "euw1", "test-key", cache, null, default);
            check(loaded.Level == 42 && loaded.Matches.Count == 1 && credentialsOk && requests.Count == 9, "Riot : assemblage du profil et match servi du cache au second chargement");
            var scoreboard = await client.LoadDetailsAsync("EUW1_123", "euw1", "", cache, default);
            check(scoreboard.Participants.Count == 3 && requests.Count == 9, "détail : participants conservés dès le chargement du profil sans appel supplémentaire");
            check(requests[0].StartsWith("https://europe.api.riotgames.com/riot/") && requests[1].StartsWith("https://euw1.api.riotgames.com/lol/summoner/"), "Riot : routage régional et plateforme");
            bool blocked = false; try { await client.LoadAsync("Test#EUW", "evil.example", "test-key", cache, null, default); } catch (ArgumentException) { blocked = true; }
            check(blocked && requests.Count == 9, "Riot : serveur inconnu refusé avant transmission de la clé");
            var clicked = await client.LoadAsync("AncienPseudo", "euw1", "test-key", cache, null, default, targetPuuid: "selected");
            check(requests[9].EndsWith("/riot/account/v1/accounts/by-puuid/selected") && clicked.RiotId == "Test#EUW" && clicked.Puuid == "selected",
                "navigation : joueur retrouvé par PUUID malgré un ancien pseudo incomplet");
            bool mismatch = false;
            try { await client.LoadAsync("Autre#TAG", "euw1", "test-key", cache, null, default, targetPuuid: "wrong-player"); }
            catch (RiotApiException) { mismatch = true; }
            check(mismatch, "navigation : identité différente renvoyée par Riot refusée");
            var listQueries = new List<string>(); int extraDetails = 0;
            for (int i = 0; i < 100; i++) cache.Set($"v2:europe:selected:EXT_{i}", match with { Id = $"EXT_{i}" });
            using var extended = new RiotProfileClient(new Handler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                string body;
                if (path.Contains("/riot/account/")) body = "{\"puuid\":\"selected\",\"gameName\":\"Test\",\"tagLine\":\"EUW\"}";
                else if (path.Contains("/summoner/")) body = "{\"summonerLevel\":42}";
                else if (path.Contains("/league/")) body = "[]";
                else if (path.EndsWith("/ids"))
                {
                    listQueries.Add(request.RequestUri.Query);
                    // Return extra IDs to also verify the client's requested bound.
                    body = JsonSerializer.Serialize(Enumerable.Range(0, 101).Select(i => $"EXT_{i}"));
                }
                else { extraDetails++; body = MatchJson; }
                return new(HttpStatusCode.OK) { Content = new StringContent(body) };
            }));
            foreach (var count in new[] { 20, 50, 100 })
            {
                var expanded = await extended.LoadAsync("Test#EUW", "euw1", "test-key", cache, null, default, matchCount: count);
                check(expanded.Matches.Count == count && expanded.RequestedMatches == count && listQueries.Last().StartsWith($"?start=0&count={count}&endTime=") && extraDetails == 0,
                    $"historique : {count} parties, requête bornée et détails en cache réutilisés");
            }
            foreach (var count in new[] { 0, 101 })
            {
                bool invalid = false;
                try { await extended.LoadAsync("Test#EUW", "euw1", "test-key", cache, null, default, matchCount: count); }
                catch (ArgumentOutOfRangeException) { invalid = true; }
                check(invalid && listQueries.Count == 3, "historique : taille invalide rejetée avant appel Riot");
            }
            for (int i = 100; i < 115; i++) cache.Set($"v2:europe:selected:EXT_{i}", match with { Id = $"EXT_{i}" });
            int identityCalls = 0; var pages = new List<string>(); bool failPage = false;
            using var paged = new RiotProfileClient(new Handler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                string body;
                if (path.EndsWith("/ids"))
                {
                    pages.Add(request.RequestUri.Query);
                    if (failPage) return new(HttpStatusCode.ServiceUnavailable);
                    var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(x => x.Split('=')).ToDictionary(x => x[0], x => x[1]);
                    int start = int.Parse(query["start"]), count = int.Parse(query["count"]);
                    body = JsonSerializer.Serialize(Enumerable.Range(start, Math.Min(count, Math.Max(0, 115 - start))).Select(i => $"EXT_{i}"));
                }
                else
                {
                    identityCalls++;
                    body = path.Contains("/account/") ? "{\"puuid\":\"selected\",\"gameName\":\"Test\",\"tagLine\":\"EUW\"}" : path.Contains("/summoner/") ? "{\"summonerLevel\":42,\"profileIconId\":685}" : "[]";
                }
                return new(HttpStatusCode.OK) { Content = new StringContent(body) };
            }));
            var first = await paged.LoadAsync("Test#EUW", "euw1", "test-key", cache, null, default, matchCount: 100);
            failPage = true; bool failed = false;
            try { await paged.LoadAsync(first.RiotId, first.Platform, "test-key", cache, null, default, matchCount: 10, previous: first); }
            catch (RiotApiException) { failed = true; }
            check(failed && first.NextStart == 100 && first.Matches.Count == 100, "pagination : erreur conserve le profil et le curseur");
            failPage = false;
            var next = await paged.LoadAsync(first.RiotId, first.Platform, "test-key", cache, null, default, matchCount: 10, previous: first);
            var last = await paged.LoadAsync(next.RiotId, next.Platform, "test-key", cache, null, default, matchCount: 10, previous: next);
            check(first.ProfileIconId == 685 && next.ProfileIconId == 685 && last.ProfileIconId == 685, "profil : icône Riot conservée pendant Voir plus");
            check(next.Matches.Count == 110 && next.NextStart == 110 && next.HasMore && last.Matches.Count == 115 && !last.HasMore && identityCalls == 3, "pagination : au-delà de 100, pages de 10, fin détectée, identité réutilisée");
            check(pages[1] == pages[2] && pages.All(q => q.EndsWith($"endTime={first.HistoryEndTime}")) && last.Matches.Select(m => m.Id).Distinct().Count() == 115, "pagination : reprise identique, borne temporelle stable et aucun doublon");
            foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests })
            {
                int calls = 0;
                using var failing = new RiotProfileClient(new Handler(_ => { calls++; return new(status); }), TimeSpan.Zero);
                bool handled = false; try { await failing.LoadAsync("Test#EUW", "euw1", "test-key", cache, null, default); } catch (RiotApiException ex) { handled = !ex.Message.Contains("test-key") && !ex.Message.Contains("Test#EUW");
                    if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        check(ex.Message.Contains($"HTTP {(int)status}") && ex.Message.Contains("compte Riot · europe"), "Riot : diagnostic authentification avec étape et région sans identité");
                }
                check(handled && calls == 1, $"Riot : HTTP {(int)status} sans répétition ni secret dans l’erreur");
                if (status == HttpStatusCode.TooManyRequests)
                {
                    using var cancel = new CancellationTokenSource(); cancel.Cancel();
                    bool canceled = false; try { await failing.LoadAsync("Test#EUW", "euw1", "test-key", cache, null, cancel.Token); } catch (OperationCanceledException) { canceled = true; }
                    check(canceled && calls == 1, "Riot : attente après quota annulable sans nouvel appel");
                }
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
