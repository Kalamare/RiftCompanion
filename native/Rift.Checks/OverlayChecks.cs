using System.Text.Json;
using System.Net;
using Rift.Core;
using Rift.Infrastructure;

static class OverlayChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var json = """[{"riotId":"Visible#EUW","team":"ORDER","rawChampionName":"game_character_displayname_Ahri","level":3,"position":"MIDDLE","runes":{"keystone":{"id":8112}},"summonerSpells":{"summonerSpellOne":{"rawDisplayName":"GeneratedTip_SummonerSpell_SummonerFlash_DisplayName"}}},{"summonerName":"Masked#TAG","team":"CHAOS","championName":"Annie"}]""";
        var roster = OverlayPlayer.Parse(JsonSerializer.Deserialize<JsonElement>(json));
        var visible = roster.Single(p => p.CanLookup);
        check(visible.Champion == "Ahri" && visible.Keystone == 8112 && visible.Spells.SequenceEqual(["SummonerFlash"]), "overlay : champion, rune et sorts lus depuis la liste officielle");
        check(roster.Single(p => !p.CanLookup).RiotId == "", "overlay : identité masquée jamais récupérée depuis summonerName");
        var now = DateTimeOffset.UtcNow;
        var match = new ProfileMatch("x", "Ahri", 420, "middle", true, false, now.AddHours(-1), 1200, 5, 2, 8, 160, 12000, 8000, 15, 8, 2, 1, 15);
        var profile = new PlayerProfile("Visible#EUW", "euw1", 30, [], [match, match with { PlayedAt = now.AddDays(-2), Win = false }], now, false, 10);
        var stats = OverlayStats.From(profile, "Ahri", now);
        check(stats.Recent12Hours.Contains("100 %") && stats.Recent30Days.Contains("50 %") && stats.Tags.Length == 0, "overlay : fenêtres temporelles sur l’échantillon et pas de badge avec moins de trois parties");
        check(OverlayStats.From(profile, "Annie", now).ChampionStats.Contains("absent"), "overlay : champion sans historique signalé sans statistique inventée");
        using var client = new LiveRosterClient(new Handler(request =>
        {
            check(request.RequestUri!.AbsoluteUri == "https://127.0.0.1:2999/liveclientdata/playerlist" && !request.Headers.Contains("X-Riot-Token") && request.Headers.Authorization is null, "overlay : destination locale fixe sans secret Riot ou LCU");
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }));
        check((await client.ReadAsync(default)).Count == 2, "overlay : liste locale chargée via transport simulé");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(run(request));
    }
}
