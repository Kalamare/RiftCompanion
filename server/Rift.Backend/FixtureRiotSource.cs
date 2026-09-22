using Rift.Core;

namespace Rift.Backend;

// Explicit demo mode only: never calls Riot and never represents real accounts.
public sealed class FixtureRiotSource : IRiotSource
{
    public int DetailCalls { get; private set; }
    public static MatchDetails Game(string id = "EUW1_900000000001", int queue = 420, bool remake = false, double seconds = 1800)
    {
        var date = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        string[] roles = ["top", "jungle", "middle", "bottom", "utility"];
        var participants = Enumerable.Range(0, 10).Select(i => new MatchParticipant("fixture-player-" + i,
            i == 0 ? "Alice#TEST" : i == 1 ? "Bob#TEST" : $"Example{i}#TEST", i < 5 ? 100 : 200, 18,
            new ProfileMatch(id, i == 0 ? "Ahri" : "Annie", queue, roles[i % 5], i < 5, remake, date, seconds,
                4, 2, 6, 180, 20000, 12000, 30, 10, 3, 2, 20) { ChampionId = i == 0 ? 103 : 1 })).ToArray();
        return new(id, queue, date, seconds, participants, []) { SchemaVersion = 4 };
    }
    public Task<RiotIdentity> Identity(string platform, string riotId, CancellationToken token)
    {
        if (platform != "euw1" || riotId is not ("Alice#TEST" or "Bob#TEST")) throw new SourceFailure("fixture_account_not_found", TimeSpan.FromMinutes(15));
        return Task.FromResult(new RiotIdentity(riotId == "Alice#TEST" ? "fixture-player-0" : "fixture-player-1", riotId, 100, 29,
            [new("RANKED_SOLO_5x5", "GOLD", "II", 45, 20, 18)]));
    }
    public Task<string[]> MatchIds(ProfileJob job, CancellationToken token)
    {
        var game = Game();
        return Task.FromResult(job.Cursor == 0 && game.PlayedAt >= job.From && game.PlayedAt < job.Until ? new[] { game.Id } : []);
    }
    public Task<MatchDetails?> Match(string region, string id, CancellationToken token)
    {
        DetailCalls++;
        return Task.FromResult<MatchDetails?>(region == "europe" && id == Game().Id ? Game() : null);
    }
}
