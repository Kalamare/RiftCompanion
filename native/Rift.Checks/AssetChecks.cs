using System.Net;
using System.Text.Json;
using Rift.Core;
using Rift.Infrastructure;

static class AssetChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const string champions = """{"data":{"MonkeyKing":{"key":"62","id":"MonkeyKing","name":"Wukong","image":{"full":"MonkeyKing.png"}},"Kaisa":{"key":"145","id":"Kaisa","name":"Kai'Sa","image":{"full":"Kaisa.png"}}}}""";
        const string items = """{"data":{"1001":{"name":"Bottes","image":{"full":"1001.png"}}}}""";
        var folder = Path.Combine(Path.GetTempPath(), "RiftAssets-" + Guid.NewGuid().ToString("N"));
        int calls = 0;
        try
        {
            using var assets = new RiotAssets(folder, new Handler(request =>
            {
                Interlocked.Increment(ref calls);
                if (request.Headers.Contains("X-Riot-Token") || request.RequestUri!.Host != "ddragon.leagueoflegends.com") throw new Exception("Unexpected asset destination/header");
                var path = request.RequestUri.AbsolutePath;
                return new(HttpStatusCode.OK) { Content = path.EndsWith(".png") ? new ByteArrayContent([137,80,78,71,13,10,26,10]) :
                    new StringContent(path.EndsWith("versions.json") ? "[\"16.18.1\"]" : path.EndsWith("champion.json") ? champions : items) };
            }));
            var match = new ProfileMatch("test", "MonkeyKing", 420, "jungle", true, false, DateTimeOffset.UtcNow, 1200, 1, 1, 1, 100, 1000, 1000, 1, 1, 1, 1, 2)
                { ChampionId = 62, Items = [1001, 1001, 0, 0, 0, 0, 0] };
            var imageCounts = new System.Collections.Concurrent.ConcurrentBag<int>();
            await assets.PrepareAsync([match, match], default, () =>
            {
                imageCounts.Add((assets.ChampionImage(62, "MonkeyKing") is null ? 0 : 1) + (assets.ItemImage(1001) is null ? 0 : 1));
                return Task.CompletedTask;
            });
            check(imageCounts.Contains(0) && imageCounts.Contains(2) && imageCounts.Count >= 3, "assets : catalogue et images publiés progressivement");
            check(assets.ChampionName(62, "wrong") == "Wukong" && assets.ChampionName(145, "Kaisa") == "Kai'Sa", "assets : noms officiels résolus par ID, Wukong et apostrophe");
            check(calls == 5 && assets.ChampionImage(62, "MonkeyKing") is not null && assets.ItemImage(1001) is not null, "assets : CDN public sans clé, icônes dédupliquées et slots vides ignorés");
            await assets.PrepareAsync([match], default);
            check(calls == 5 && assets.ItemName(1001) == "Bottes", "assets : catalogue et images en cache, aucun téléchargement répété");
            await assets.PrepareAsync([match], default, profileIconId: 685);
            check(calls == 6 && assets.ProfileIconImage(685)?.EndsWith("685.png") == true && assets.ProfileIconImage(null) is null,
                "profil : icône officielle téléchargée une seule fois, identité inconnue sans image inventée");
            await assets.PrepareAsync([match], default, profileIconId: 685);
            check(calls == 6, "profil : icône déjà en cache sans nouvel appel");
            using var offline = new RiotAssets(folder, new Handler(_ => throw new HttpRequestException()));
            await offline.PrepareAsync([match], default);
            check(offline.ChampionName(62, "MonkeyKing") == "Wukong" && offline.ItemImage(1001) is not null, "assets : réouverture hors ligne depuis le cache");
            File.SetLastWriteTimeUtc(Path.Combine(folder, "versions.json"), DateTime.UtcNow.AddDays(-3));
            using var localOnly = new RiotAssets(folder, new Handler(_ => throw new Exception("No network allowed")));
            await localOnly.PrepareAsync([match], default, offlineOnly: true, profileIconId: 685);
            check(localOnly.ProfileIconImage(685) is not null && localOnly.ProfileIconImage(999) is null, "profil : icône restaurée hors ligne et aucune substitution entre joueurs");
            check(localOnly.ChampionName(62, "MonkeyKing") == "Wukong" && localOnly.ItemImage(1001) is not null, "profil personnel : illustrations restaurées sans réseau même avec catalogue ancien");
            using var emptyLocal = new RiotAssets(Path.Combine(folder, "empty"), new Handler(_ => throw new Exception("No network allowed")));
            await emptyLocal.PrepareAsync([match], default, offlineOnly: true);
            check(emptyLocal.ChampionImage(62, "MonkeyKing") is null, "profil personnel : cache illustrations absent sans appel réseau ni blocage");
            using var doc = JsonDocument.Parse(champions.Replace("MonkeyKing.png", "../outside.png"));
            check(RiotAssets.ParseCatalog(doc.RootElement, true).All(x => x.Id != 62), "assets : chemin image hors catalogue refusé");
            using var raw = JsonDocument.Parse("""{"metadata":{"matchId":"x"},"info":{"queueId":420,"mapId":11,"gameDuration":1200,"participants":[{"puuid":"p","championId":62,"championName":"MonkeyKing","item0":1001,"item6":3364}]}}""");
            var parsed = ProfileParser.Match(raw.RootElement, "p")!;
            check(parsed.ChampionId == 62 && parsed.Items.SequenceEqual(new[] {1001,0,0,0,0,0,3364}), "assets : ID champion et ordre des sept slots conservés depuis Match-v5");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
}
