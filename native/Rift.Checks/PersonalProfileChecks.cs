using System.Text.Json;
using Rift.Core;
using Rift.Infrastructure;

static class PersonalProfileChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
        var summoner = Json("""{"gameName":"Mon compte","tagLine":"CUSTOM","summonerLevel":123,"profileIconId":685}""");
        var owner = PersonalProfileDetector.Parse(summoner, Json("""{"region":"EUW","locale":"en_US"}"""))!;
        check(owner.RiotId == "Mon compte#CUSTOM" && owner.Platform == "euw1" && owner.Level == 123,
            "profil personnel : Riot ID et serveur détectés sans déduire le serveur du tag ou de la langue");
        check(owner.ProfileIconId == 685, "profil : icône extraite du compte LoL connecté");
        var link = ProfileLinks.LeagueOfGraphs("Nom / é?#TAG", "euw1")!;
        check(link.Host == "www.leagueofgraphs.com" && link.Query.Length == 0 && link.Fragment.Length == 0 &&
            link.AbsoluteUri.Contains("%2F") && ProfileLinks.LeagueOfGraphs("SansTag", "euw1") is null &&
            ProfileLinks.LeagueOfGraphs("Nom#TAG", "unknown") is null, "profil : lien externe encodé, serveur validé et destination fixe");
        check(PersonalProfileDetector.Parse(Json("""{"displayName":"Ancien pseudo"}"""), Json("""{"region":"EUW"}""")) is null &&
            PersonalProfileDetector.Parse(summoner, Json("""{"region":"PBE"}""")) is null,
            "profil personnel : identité incomplète et serveur non pris en charge non inventés");
        var credentials = Credentials.Parse("LeagueClient:123:4567:test-only:https");
        int calls = 0;
        bool available = false;
        using var detector = new PersonalProfileDetector(new Transport((_, endpoint, _) =>
        {
            calls++;
            return Task.FromResult<JsonElement?>(endpoint == "/lol-summoner/v1/current-summoner" ? summoner : Json("""{"region":"EUW"}"""));
        }), _ => Task.FromResult<Credentials?>(available ? credentials : null));
        check(await detector.DetectAsync(default) is null && calls == 0, "profil personnel : client absent, aucune requête");
        available = true;
        check(PersonalProfileStore.SameAccount(await detector.DetectAsync(default), owner) && calls == 2,
            "profil personnel : client ouvert plus tard détecté avec deux lectures locales");
        using var failing = new PersonalProfileDetector(new Transport((_, _, _) => throw new HttpRequestException("secret-test")), _ => Task.FromResult<Credentials?>(credentials));
        check(await failing.DetectAsync(default) is null, "profil personnel : erreur locale récupérable sans fuite de secret");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var waiting = new PersonalProfileDetector(new Transport(async (_, _, token) => { await Task.Delay(1000, token); return null; }), _ => Task.FromResult<Credentials?>(credentials));
        bool stopped = false;
        try { await waiting.DetectAsync(canceled.Token); } catch (OperationCanceledException) { stopped = true; }
        check(stopped, "profil personnel : détection annulable à la fermeture");
        var folder = Path.Combine(Path.GetTempPath(), "RiftPersonal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PersonalProfileStore(folder);
            store.Save(owner);
            var match = new ProfileMatch("m", "MonkeyKing", 420, "jungle", true, false, DateTimeOffset.UtcNow, 1200, 1, 2, 3, 100, 1000, 1000, 5, 1, 1, 1, 4);
            var snapshot = owner with { Matches = Enumerable.Range(0, 115).Select(i => match with { Id = "m" + i }).ToArray(), Ranks = [new("RANKED_SOLO_5x5", "GOLD", "I", 50, 20, 10)], RequestedMatches = 120, NextStart = 120, HasMore = true, HistoryEndTime = 123, Puuid = "test-puuid" };
            store.Save(snapshot);
            var restored = new PersonalProfileStore(folder).Read()!;
            check(restored.ProfileIconId == 685, "profil : icône conservée au redémarrage");
            check(restored.Matches.Count == 115 && restored.NextStart == 120 && restored.HistoryEndTime == 123 && restored.Ranks.Single().Lp == 50,
                "profil personnel : profil complet et pagination restaurés au prochain lancement sans Riot");
            new ProfileHistory(folder).Remember("Autre#NA", "na1");
            check(PersonalProfileStore.SameAccount(store.Read(), owner), "profil personnel : les recherches récentes ne remplacent pas le compte personnel");
            store.Save(owner with { RiotId = "Second#TAG", Matches = [], Ranks = [] });
            check(store.Read() is { RiotId: "Second#TAG", Matches.Count: 0, Ranks.Count: 0 }, "profil personnel : changement de compte sans réutiliser les statistiques du précédent");
            bool rejected = false;
            try { store.Save(owner with { Demo = true }); } catch (ArgumentException) { rejected = true; }
            check(rejected && store.Read()!.RiotId == "Second#TAG", "profil personnel : une démonstration ne remplace pas le compte");
            File.WriteAllText(Path.Combine(folder, "personal-profile.json"), "{broken");
            check(store.Read() is null, "profil personnel : sauvegarde corrompue ignorée");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class Transport(Func<Credentials, string, CancellationToken, Task<JsonElement?>> get) : ILcuTransport
    {
        public Task<JsonElement?> GetAsync(Credentials credentials, string endpoint, CancellationToken token) => get(credentials, endpoint, token);
        public void Dispose() { }
    }
}
