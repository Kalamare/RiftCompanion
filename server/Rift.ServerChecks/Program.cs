using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Rift.Backend;
using Rift.Contracts;
using Rift.Core;

var options = BackendOptions.Load(new ConfigurationBuilder().AddEnvironmentVariables().Build());
var schema = "checks_" + Guid.NewGuid().ToString("N"); // Generated identifier, never external input.
await using var admin = NpgsqlDataSource.Create(options.ConnectionString);
await using (var create = admin.CreateCommand($"CREATE SCHEMA {schema}")) await create.ExecuteNonQueryAsync();
var connectionString = new NpgsqlConnectionStringBuilder(options.ConnectionString) { SearchPath = schema }.ConnectionString;
await using var source = NpgsqlDataSource.Create(connectionString);
var store = new RiftStore(source, options with { HistoryBackfillEnabled = true }); var token = CancellationToken.None;
var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
var until = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
int checks = 0;
void Check(bool condition, string name)
{ if (!condition) throw new InvalidOperationException("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
async Task<long> Count(string table)
{ await using var command = source.CreateCommand("SELECT count(*) FROM " + table); return (long)(await command.ExecuteScalarAsync())!; }
async Task Sql(string text)
{ await using var command = source.CreateCommand(text); await command.ExecuteNonQueryAsync(); }
try
{
    using (var schemaStream = typeof(RiftStore).Assembly.GetManifestResourceStream("Rift.Backend.schema.sql")!)
    using (var schemaReader = new StreamReader(schemaStream))
    {
        var sql = await schemaReader.ReadToEndAsync();
        var migrationStart = sql.IndexOf("-- v2:", StringComparison.Ordinal);
        var migrationEnd = sql.IndexOf("CREATE TABLE IF NOT EXISTS unavailable_matches", StringComparison.Ordinal);
        await Sql(sql.Remove(migrationStart, migrationEnd - migrationStart));
        await Sql("INSERT INTO matches VALUES('europe','MIGRATION',now(),'{}'); INSERT INTO profile_jobs(id,platform,riot_id,normalized_id,active_until,coverage_from,window_from,window_until,cursor) VALUES('00000000-0000-0000-0000-000000000001','euw1','Migration#TEST','MIGRATION#TEST',now(),now(),now(),now(),40)");
        await store.Initialize(token);
        await using var migrated = source.CreateCommand("SELECT cursor FROM profile_jobs WHERE riot_id='Migration#TEST' AND kind='history'");
        Check((int)(await migrated.ExecuteScalarAsync())! == 40 && await Count("matches") == 1, "v1 to v2 migration preserves history cursor and stored matches");
        await Sql("DELETE FROM profile_jobs; DELETE FROM matches");
    }
    await store.Initialize(token); await store.Initialize(token);
    var fixture = new FixtureRiotSource();
    var collector = new ProfileCollector(store, fixture, NullLogger<ProfileCollector>.Instance);
    var alice = await store.Lookup(new("euw1", "Alice#TEST"), from, token);
    var again = await store.Lookup(new("EUW1", "alice#test"), from, token);
    Check(alice == again && await Count("profile_jobs") == 1, "Concurrent/repeated requests share a durable job");
    await collector.Tick(token);
    Check(await Count("matches") == 1 && await Count("contributions") == 10 && fixture.DetailCalls == 1, "One downloaded match enriches ten players");
    Check(await Count("profile_jobs") == 2, "Only the explicit profile gets independent recent/history jobs");
    var aliceState = (await store.LookupState(alice, token))!;
    Check(aliceState.Status == "ready" && aliceState.Player!.Coverage.AccessibleHistoryScanned, "Requested accessible history scanned and persisted");
    var passive = (await store.Player("euw1", "fixture-player-1", token))!;
    Check(passive.Coverage.Status == "encountered_only" && !passive.Coverage.AccessibleHistoryScanned && passive.RanksUpdatedAt is null && passive.Level is null,
        "Incidental profile remains partial with no invented rank or account level");
    var expected = ProfileSummary.From([FixtureRiotSource.Game().Participants[0].Stats]);
    var snapshot = (await store.Stats("euw1", "fixture-player-0", from, until, 420, token))!;
    Check(snapshot.Summary == expected && snapshot.Champions.Length == 1 && snapshot.Roles.Length == 1, "Daily server aggregates match domain formulas");
    var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.Ingest("europe", FixtureRiotSource.Game(), token)));
    Check(results.All(x => !x) && await Count("contributions") == 10, "Concurrent replay cannot double count a match");
    var bob = await store.Lookup(new("euw1", "Bob#TEST"), from, token);
    for (int i = 0; i < 4 && (await store.LookupState(bob, token))!.Status != "ready"; i++) await collector.Tick(token);
    Check((await store.LookupState(bob, token))!.Status == "ready" && fixture.DetailCalls == 1 && await Count("profile_jobs") == 4,
        "Second participant's explicit lookup reuses existing match details");
    var aram = FixtureRiotSource.Game("EUW1_900000000002", 450, seconds: 600);
    await store.Ingest("europe", aram, token);
    await store.Ingest("europe", FixtureRiotSource.Game("EUW1_900000000003", remake: true), token);
    var all = (await store.Stats("euw1", "fixture-player-0", from, until, 0, token))!;
    Check(all.Summary == ProfileSummary.From([FixtureRiotSource.Game().Participants[0].Stats, aram.Participants[0].Stats]) && all.Summary.CsPerMinute == 9,
        "Weighted duration rates; remakes excluded");
    Check(all.Roles.Sum(x => x.Summary.Games) == 1 && (await store.Stats("euw1", "fixture-player-0", from, until, -1, token))!.Summary.Games == 1,
        "Queue filters and standard-role eligibility remain independent");
    var arena = FixtureRiotSource.Game("EUW1_900000000004", 1700);
    await store.Ingest("europe", arena, token);
    Check((await store.Stats("euw1", "fixture-player-0", from, until, 1700, token))!.Summary.Participation is null,
        "Arena has no invented standard-team kill participation");
    // Fail after inserting a match and player to prove the whole transaction rolls back.
    await Sql("CREATE FUNCTION fail_stats() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.champion_id=999999 THEN RAISE EXCEPTION 'synthetic failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_stats BEFORE INSERT ON daily_stats FOR EACH ROW EXECUTE FUNCTION fail_stats();");
    var broken = FixtureRiotSource.Game("EUW1_900000000005");
    broken = broken with { Participants = broken.Participants.Select(p => p with { Stats = p.Stats with { ChampionId = 999999 } }).ToArray() };
    try { await store.Ingest("europe", broken, token); throw new InvalidOperationException("Expected database failure"); }
    catch (PostgresException) { }
    Check(!await store.HasMatch("europe", broken.Id, token) && await Count("contributions") == 40, "Failure rolls back match, contributions and aggregates together");
    await Sql("DROP TRIGGER fail_stats ON daily_stats; DROP FUNCTION fail_stats();");
    await Sql("UPDATE profile_jobs SET due_at=now()+interval '1 hour'; UPDATE profile_jobs SET due_at=now()-interval '1 second',state='ready',window_until=now()-interval '1 day' WHERE riot_id='Alice#TEST' AND kind='recent'");
    var incremental = (await store.Claim(token))!;
    Check(incremental.Kind == "recent" && incremental.Until > DateTimeOffset.UtcNow.AddMinutes(-1), "Incremental scan ends at the new execution time");
    await store.Fail(incremental, "rate_limited", TimeSpan.FromMinutes(1), token);
    Check(await store.Claim(token) is null, "Backoff survives release of the job lease");
    await store.Lookup(new("euw1", "Alice#TEST"), from, token);
    Check(await store.Claim(token) is null, "Repeated lookup cannot bypass backoff");
    await store.SetUnavailable(incremental.Id, "EUW1_missing", true, token);
    await store.SetUnavailable(incremental.Id, "EUW1_missing", true, token);
    await store.CompletePage(incremental, 0, token);
    Check(await Count("unavailable_matches") == 1, "Repeated unavailable detail counted only once");
    await store.SetUnavailable(incremental.Id, "EUW1_missing", false, token);
    await store.CompletePage(incremental, 0, token);
    Check(await Count("unavailable_matches") == 0, "Recovered detail clears partial-gap counter");
    await store.PauseRequests("europe", TimeSpan.FromSeconds(20), token);
    Check(await store.ReserveRequest("europe", token) > TimeSpan.FromSeconds(15), "Shared Riot cooldown stored in PostgreSQL");
    await store.ObserveLimit("euw1", 60, 1, 0, token);
    Check(await store.ReserveRequest("euw1", token) <= TimeSpan.Zero && await store.ReserveRequest("euw1", token) > TimeSpan.FromSeconds(55),
        "Observed lower method quota constrains subsequent host requests");
    var leader = (await store.TryCollectorLock(token))!;
    Check(await store.TryCollectorLock(token) is null, "Only one collector can download details at a time");
    await store.ReleaseCollectorLock(leader);
    var changes = await store.Changes(0, token);
    Check(changes.Length > 0 && changes.Select(c => c.Sequence).Distinct().Count() == changes.Length, "Durable notification outbox has unique ordered versions");
    Check(await store.Player("euw1", "not-seen", token) is null, "Unknown profile is not represented as zero games");
    var page = (await store.Profile("euw1", "fixture-player-0", 0, 1, 0, token))!;
    var next = (await store.Profile("euw1", "fixture-player-0", page.NextStart, 1, page.HistoryEndTime, token))!;
    Check(page.HasMore && page.Matches.Count == 1 && page.Matches[0].Id != next.Matches[0].Id, "Stored history pagination has stable ordering and bounded pages");
    await Sql("UPDATE profile_jobs SET due_at=now()+interval '1 hour',lease_until=null,state='ready',refresh_after=null");
    await store.Lookup(new("euw1", "Alice#TEST"), from, token);
    Check(await store.Claim(token) is null, "Consultation does not schedule an immediate Riot refresh");
    var refreshes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => store.Refresh("euw1", "fixture-player-0", true, TimeSpan.FromMinutes(5), token)));
    Check(refreshes.Count(x => x!.Accepted) == 1 && refreshes.All(x => x!.Status == "pending"), "Concurrent refresh requests share exactly one job");
    var refresh = (await store.Claim(token))!;
    Check(refresh.Kind == "recent" && refresh.Puuid == "fixture-player-0", "Refresh schedules recent history only");
    await store.CompletePage(refresh, 0, token);
    Check((await store.Refresh("euw1", "fixture-player-0", true, TimeSpan.FromMinutes(5), token))!.Status == "cooldown", "Cooldown survives completion and rejects immediate repetition");
    var fresh = await store.Lookup(new("euw1", "New#TEST"), from, token);
    var priority = (await store.Claim(token))!;
    Check(priority.Id == fresh, "New identity selected while other jobs are not due");
    await store.CompletePage(priority, RiftStore.HistoryPageSize, token);
    await Sql("UPDATE profile_jobs SET due_at=now()-interval '1 second' WHERE riot_id='New#TEST'");
    var resumed = (await store.Claim(token))!;
    Check(resumed.Id == fresh && resumed.Cursor == RiftStore.HistoryPageSize && resumed.Until == priority.Until,
        "History batches preserve their cursor and time boundary independently of recent jobs");
    bool limited = false;
    try { await store.Lookup(new("euw1", "Fourth#TEST"), from, token); } catch (WorkLimit) { limited = true; }
    Check(limited, "New-profile budget is persistent and separate from cached consultation");
    await store.Lookup(new("euw1", "Alice#TEST"), from, token);
    for (int i = 1; i <= 5; i++) await store.Refresh("euw1", "fixture-player-" + i, true, TimeSpan.FromMinutes(5), token);
    Check((await store.Refresh("euw1", "fixture-player-6", true, TimeSpan.FromMinutes(5), token))!.Status == "limited", "Per-operator refresh budget bounds distinct profiles");
    await Sql("UPDATE profile_jobs SET due_at=now()+interval '1 hour',lease_until=null; UPDATE profile_jobs SET due_at=now()-interval '1 minute',scanned=false,state='queued',cursor=60 WHERE puuid='fixture-player-0'; UPDATE collector_turn SET value=0");
    var kinds = new List<string>();
    for (int i = 0; i < 4; i++)
    {
        var nextJob = (await store.Claim(token))!; kinds.Add(nextJob.Kind);
        await store.Fail(nextJob, "test", TimeSpan.Zero, token);
    }
    Check(kinds.Count(k => k == "history") == 1 && kinds.Count(k => k == "recent") == 3, "Historical backfill retains a turn under sustained recent work");
    await store.Initialize(token);
    Check(await Count("matches") == 4, "Idempotent v2 migration retains existing match data");
    var bounded = new RiftStore(source, options with { HistoryBackfillEnabled = false });
    var sampleSource = new SampleSource();
    var sampleCollector = new ProfileCollector(bounded, sampleSource, NullLogger<ProfileCollector>.Instance);
    await Sql("UPDATE profile_jobs SET due_at=now()+interval '1 hour',lease_until=null; UPDATE profile_jobs SET due_at=now()-interval '1 minute',state='queued' WHERE puuid='fixture-player-0'");
    await sampleCollector.Tick(token);
    Check(sampleSource.Calls == 20 && await Count("matches") == 24, "Sample collector fetches at most twenty details without importing the season");
    Check(await bounded.Claim(token) is null, "Full recent page does not enqueue the rest of the season or resume old history");
    await Sql("DELETE FROM schema_version; INSERT INTO schema_version VALUES(99)");
    bool refusedFuture = false;
    try { await store.Initialize(token); } catch (InvalidOperationException) { refusedFuture = true; }
    Check(refusedFuture, "Future database schema refused without migration");
    Console.WriteLine($"{checks} PostgreSQL integration checks passed.");
}
finally
{
    await source.DisposeAsync();
    await using var drop = admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");
    await drop.ExecuteNonQueryAsync();
}

sealed class SampleSource : IRiotSource
{
    public int Calls { get; private set; }
    public Task<RiotIdentity> Identity(string platform, string riotId, CancellationToken token) => new FixtureRiotSource().Identity(platform, riotId, token);
    public Task<string[]> MatchIds(ProfileJob job, CancellationToken token)
    {
        if (job.Cursor != 0 || job.From != DateTimeOffset.UnixEpoch) throw new Exception("Sample did not query latest matches independently of season date");
        return Task.FromResult(Enumerable.Range(0,20).Select(i => "EUW1_9100000000" + i.ToString("00")).ToArray());
    }
    public Task<MatchDetails?> Match(string region, string id, CancellationToken token)
    { Calls++; return Task.FromResult<MatchDetails?>(FixtureRiotSource.Game(id)); }
}
