using System.Net;
using System.Text.Json;
using Rift.Core;
using Rift.Infrastructure;

static class MatchDetailsChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var json = JsonSerializer.Serialize(new {
            metadata = new { matchId = "EUW1_42" },
            info = new { queueId = 420, mapId = 11, gameDuration = 1200, gameCreation = 1760000000000L,
                participants = Enumerable.Range(0, 10).Select(i => new { puuid = $"p{i}", riotIdGameName = $"Joueur{i}", riotIdTagline = "EUW",
                    teamId = i < 5 ? 100 : 200, teamPosition = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" }[i % 5],
                    champLevel = 15, championName = "Vi", championId = 254, summoner1Id = 4, summoner2Id = 11, roleBoundItem = 1103,
                    perks = new { styles = new[] {
                        new { description = "subStyle", style = 8400, selections = new[] { new { perk = 8473 }, new { perk = 8242 } } },
                        new { description = "primaryStyle", style = 8000, selections = new[] { new { perk = 8005 }, new { perk = 9111 }, new { perk = 9104 }, new { perk = 8014 } } }
                    } }, kills = i, deaths = 2, assists = 3,
                    win = i < 5, totalMinionsKilled = 100, neutralMinionsKilled = 20, item0 = 3047, item6 = 3364 }),
                teams = new[] { new { teamId = 100, win = true, bans = new[] { new { championId = -1, pickTurn = 2 }, new { championId = 157, pickTurn = 1 } }, objectives = new { tower = new { kills = 5 } } },
                    new { teamId = 200, win = false, bans = Array.Empty<object>(), objectives = new { tower = new { kills = 1 } } } as object }
            }
        });
        var details = MatchDetails.Parse(JsonSerializer.Deserialize<JsonElement>(json))!;
        check(details.Participants.Count == 10 && details.Teams.Count == 2 && details.Participants[1].RiotId == "Joueur1#EUW" && details.Participants[1].Level == 15, "détail : dix participants, Riot ID et niveau");
        check(details.SchemaVersion == 4 && details.Participants.All(p => p.Spells.SequenceEqual([4, 11])), "détail : deux sorts d’invocateur conservés dans leur ordre");
        check(details.Participants[0].RoleBoundItem == 1103 && details.Participants[0].Runes is { PrimaryStyle: 8000, Keystone: 8005, SecondaryStyle: 8400 } &&
            details.Participants[0].Runes.Selections.Length == 6, "détail : quête et six runes, arbres identifiés indépendamment de leur ordre");
        check(details.Participants[0].Runes.PrimarySelections.SequenceEqual([8005, 9111, 9104, 8014]) &&
            details.Participants[0].Runes.SecondarySelections.SequenceEqual([8473, 8242]), "détail : sélections primaires et secondaires conservées séparément");
        var missing = MatchDetails.Parse(JsonSerializer.Deserialize<JsonElement>(json.Replace("\"roleBoundItem\":1103", "\"unrelated\":1103").Replace("\"perks\":", "\"unrelatedPerks\":")))!;
        check(missing.Participants[0].RoleBoundItem is null && missing.Participants[0].Runes.Keystone == 0 && missing.Participants[0].Runes.Selections.Length == 0,
            "détail : quête et runes absentes jamais déduites du rôle");
        check(details.Participants[1].Stats.TeamKills == 10 && details.Participants[1].Stats.Participation == 40 && details.Participants[1].Stats.Cs == 120 && details.Participants[1].Stats.Items.Length == 7, "détail : statistiques par équipe et sept objets");
        check(details.Teams[0].Bans.SequenceEqual([157, -1]) && details.Teams[0].Objectives["tower"] == 5 && !details.Teams[0].Objectives.ContainsKey("dragon"), "détail : bans ordonnés, absence distincte du zéro");
        var aram = MatchDetails.Parse(JsonSerializer.Deserialize<JsonElement>(json.Replace("\"queueId\":420", "\"queueId\":450")))!;
        check(aram.Participants.All(p => p.Stats.Role.Length == 0), "détail : aucun rôle classique inventé en ARAM");
        var arena = MatchDetails.Parse(JsonSerializer.Deserialize<JsonElement>(json.Replace("\"queueId\":420", "\"queueId\":1700").Replace("\"teamId\":100", "\"playerSubteamId\":3,\"teamId\":100")))!;
        check(arena.Teams.Any(t => t.Id == 3) && arena.Teams.All(t => t.Win is null && t.Objectives.Count == 0), "détail : sous-équipes sans faux objectifs ni résultat d’équipe");
        check(MatchDetails.Parse(JsonSerializer.Deserialize<JsonElement>("{}")) is null && MatchDetails.Parse(JsonSerializer.Deserialize<JsonElement>(json.Replace("\"queueId\":420", "\"queueId\":0"))) is null, "détail : document incomplet et partie personnalisée exclus");
        var directory = Path.Combine(Path.GetTempPath(), "RiftDetails-" + Guid.NewGuid().ToString("N"));
        try
        {
            int calls = 0;
            var cache = new ProfileCache(Path.Combine(directory, "cache.db"));
            using var api = new RiotProfileClient(new Handler(request => {
                calls++;
                if (request.RequestUri!.Host != "europe.api.riotgames.com" || !request.RequestUri.AbsolutePath.EndsWith("/EUW1_42")) throw new Exception("Unexpected match route");
                return new(HttpStatusCode.OK) { Content = new StringContent(json) };
            }));
            await api.LoadDetailsAsync("EUW1_42", "euw1", "test-key", cache, default);
            var cached = await api.LoadDetailsAsync("EUW1_42", "euw1", "", new ProfileCache(Path.Combine(directory, "cache.db")), default);
            check(calls == 1 && cached.Participants.Count == 10 && cached.Teams[0].Objectives["tower"] == 5, "détail : premier appel puis réouverture hors ligne sans clé");
            check(cached.Participants[0].Spells.SequenceEqual([4, 11]), "détail : sorts restaurés depuis SQLite");
            bool rejected = false;
            try { await api.LoadDetailsAsync("EUW1_43", "euw1", "", cache, default); } catch (RiotApiException) { rejected = true; }
            check(rejected && calls == 1 && cache.GetDetails("asia", "EUW1_42") is null, "détail : clé absente expliquée et régions isolées");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            bool cancelled = false;
            try { await api.LoadDetailsAsync("EUW1_42", "euw1", "test-key", cache, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && calls == 1, "détail : annulation respectée même avec cache");
            foreach (var code in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
            {
                int errors = 0; using var failed = new RiotProfileClient(new Handler(_ => { errors++; return new(code); }));
                bool caught = false;
                try { await failed.LoadDetailsAsync("EUW1_99", "euw1", "test-key", cache, default); } catch (RiotApiException ex) { caught = !ex.Message.Contains("test-key"); }
                check(caught && errors == 1 && cache.GetDetails("europe", "EUW1_99") is null, $"détail : HTTP {(int)code} sans répétition ni faux cache");
            }
            cache.SetDetails("europe", cached with { SchemaVersion = 0, Participants = cached.Participants.Select(p => p with { Spells = [] }).ToArray() });
            var legacy = await api.LoadDetailsAsync("EUW1_42", "euw1", "", cache, default);
            check(legacy.SchemaVersion == 0 && calls == 1, "détail : ancien cache reste consultable sans clé");
            var upgraded = await api.LoadDetailsAsync("EUW1_42", "euw1", "test-key", cache, default);
            check(upgraded.SchemaVersion == 4 && upgraded.Participants[0].Spells.SequenceEqual([4, 11]) && calls == 2, "détail : ancien cache enrichi des sorts au premier accès autorisé");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
