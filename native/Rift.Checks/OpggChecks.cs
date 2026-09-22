using System.Net;
using System.Text.Json;
using Rift.Infrastructure;

static class OpggChecks
{
    private const string Payload = """
class LolGetSummonerProfile: data
class Data: summoner
class Summoner: game_name,tagline,region,updated_at,most_champions,ladder_rank
class MostChampions: game_type,season_id,year,champion_stats
class ChampionStat: id,play,win,lose,kill,death,assist,champion_name
class LadderRank: rank,total

LolGetSummonerProfile(Data(Summoner("Example","EUW",null,"2026-09-21T07:55:12+09:00",MostChampions("RANKED",33,null,[ChampionStat(36,113,64,49,674,508,1038,"Dr. Mundo")]),LadderRank(124364,3426816))))
""";
    public static async Task Run(Action<bool, string> check)
    {
        var parsed = OpggPayload.Parse(Payload, "Example#EUW", "euw1", DateTimeOffset.UtcNow);
        check(parsed.Rank == 124364 && parsed.Total == 3426816 && parsed.Season == 33 && parsed.Queue == "RANKED", "OP.GG : rang régional, population et périmètre saison parsés depuis le format réel");
        var champion = parsed.Champions.Single();
        var seasonal = OpggPayload.Parse(Payload.Replace("game_type,season_id,year,champion_stats", "game_type,season_id,year,play,win,lose,champion_stats").Replace("33,null,[", "33,null,692,353,339,["), "Example#EUW", "euw1", DateTimeOffset.UtcNow);
        check(seasonal.SeasonGames == 692 && seasonal.SeasonWins == 353 && !seasonal.HasCompleteChampions, "OP.GG : bilan saisonnier indépendant de la liste partielle de champions");
        check(champion.Games == 113 && Math.Abs(champion.AverageKills - 674.0 / 113) < .001, "OP.GG : compteurs saisonniers convertis en KDA moyen, pas confondus avec des moyennes");
        foreach (var bad in new[] { Payload.Replace("Example", "Other"), Payload.Replace("null,\"2026", "\"KR\",\"2026"), Payload + "evil()", Payload.Replace("class LadderRank: rank,total", "class LadderRank: rank,rank") })
        {
            bool rejected = false; try { OpggPayload.Parse(bad, "Example#EUW", "euw1", DateTimeOffset.UtcNow); } catch (JsonException) { rejected = true; }
            check(rejected, "OP.GG : identité, région ou structure incohérente rejetée");
        }
        check(OpggPayload.Parse(Payload.Replace("LadderRank(124364,3426816)", "LadderRank(null,null)"), "Example#EUW", "euw1", DateTimeOffset.UtcNow).Rank is null, "OP.GG : classement absent distinct du rang zéro");
        var directory = Path.Combine(Path.GetTempPath(), "RiftOpggChecks-" + Guid.NewGuid().ToString("N"));
        int calls = 0; bool offline = false;
        using var client = new OpggClient(directory, new Handler(request =>
        {
            calls++;
            check(request.RequestUri!.AbsoluteUri == "https://mcp-api.op.gg/mcp" && request.Headers.Authorization is null && !request.Headers.Contains("X-Riot-Token"), "OP.GG : destination fixe, aucune clé Riot ou LCU transmise");
            if (offline) return new(HttpStatusCode.TooManyRequests);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, result = new { content = new[] { new { type = "text", text = Payload } } } })) };
        }));
        try
        {
            var one = await client.LoadAsync("Example#EUW", "euw1", default);
            var two = await client.LoadAsync("Example#EUW", "euw1", default);
            check(one?.Rank == two?.Rank && calls == 1, "OP.GG : cache persistant une heure, pas de second appel");
            var path = Directory.GetFiles(Path.Combine(directory, "opgg-cache-v2"), "*.json").Single();
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { FetchedAt = DateTimeOffset.UtcNow.AddHours(-2), Text = Payload }));
            offline = true;
            var stale = await client.LoadAsync("Example#EUW", "euw1", default);
            await client.LoadAsync("Example#EUW", "euw1", default);
            check(stale?.Rank == one?.Rank && calls == 2 && stale!.FetchedAt < DateTimeOffset.UtcNow.AddHours(-1), "OP.GG : quota conserve le cache daté et suspend les appels suivants");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool stopped = false; try { await client.LoadAsync("Example#EUW", "euw1", cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
            check(stopped && calls == 2 && await client.LoadAsync("Annie#BOT", "euw1", default) is null, "OP.GG : annulation et bots sans réseau");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(run(request)); }
}
