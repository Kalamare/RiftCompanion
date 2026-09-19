using System.Text.Json;
using Microsoft.Data.Sqlite;
using Rift.Core;
using Rift.Infrastructure;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("ÉCHEC : " + name); Console.WriteLine("OK " + name); passed++; }
JsonElement Json(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
var credentials = Credentials.Parse("LeagueClient:123:4567:secret-test:https");
Check(credentials.Port == 4567 && !credentials.ToString()!.Contains("secret-test"), "lockfile valide et secret absent de ToString");
foreach (var input in new[] { "LeagueClient:1:0:x:https", "LeagueClient:1:65536:x:https", "LeagueClient:1:34:x:http", "LeagueClient:1:34::https" })
{
    var rejected = false; try { Credentials.Parse(input); } catch (FormatException) { rejected = true; }
    Check(rejected, "lockfile invalide rejeté");
}
var session = Json("""{"localPlayerCellId":0,"myTeam":[{"cellId":0,"championId":64,"assignedPosition":"jungle","puuid":"private-user"}],"theirTeam":[],"bans":{"myTeamBans":[0,-1,35]}}""");
var draft = DraftParser.Parse(session)!;
Check(draft.Allies[0].IsYou && draft.Allies[0].Role == "jungle" && draft.AllyBans.SequenceEqual([35]), "rôle, joueur local et bans filtrés");
Check(!JsonSerializer.Serialize(draft).Contains("private-user"), "identités exclues du domaine");
Check(DraftParser.Parse(Json("{}")) is null, "session incomplète signalée");
Check(!DraftParser.Parse(Json("""{"myTeam":[{}],"theirTeam":[]}"""))!.Allies[0].IsYou, "cellule locale manquante non identifiée à tort");

var phase = "ChampSelect";
var fail = false;
var fake = new FakeTransport((_, endpoint, _) =>
{
    if (fail) throw new HttpRequestException("secret-test");
    JsonElement? result = endpoint.EndsWith("gameflow-phase") ? Json(JsonSerializer.Serialize(phase)) : endpoint.EndsWith("/session") ? session : Json("""[{"id":64,"name":"Lee Sin"}]""");
    return Task.FromResult(result);
});
using (var monitor = new LcuMonitor(fake, _ => Task.FromResult<Credentials?>(credentials)))
{
    await monitor.TickAsync(default); await monitor.TickAsync(default);
    Check(monitor.State.Draft?.Allies[0].ChampionId == 64 && monitor.Requests == 5, "draft chargée et catalogue mis en cache");
    phase = "InProgress"; var before = monitor.Requests; await monitor.TickAsync(default);
    Check(monitor.State.Draft is null && monitor.IntervalMs == 15000 && monitor.Requests == before + 1, "en partie : draft effacée et requêtes réduites");
    fail = true; await monitor.TickAsync(default);
    Check(!monitor.State.Connected && monitor.State.Draft is null && !monitor.State.Message.Contains("secret-test"), "erreur réseau sans fuite ni état périmé");
    fail = false; phase = "Lobby"; await monitor.TickAsync(default); Check(monitor.State.Connected, "reconnexion après erreur");
}
using (var offline = new LcuMonitor(new FakeTransport((_, _, _) => throw new Exception("unexpected")), _ => Task.FromResult<Credentials?>(null)))
{ await offline.TickAsync(default); Check(offline.Requests == 0, "client absent : aucune requête"); }
var active = 0; var maximum = 0;
using (var serial = new LcuMonitor(new FakeTransport(async (_, endpoint, token) =>
{
    active++; maximum = Math.Max(maximum, active);
    try { await Task.Delay(10, token); return endpoint.EndsWith("gameflow-phase") ? Json("\"InProgress\"") : null; }
    finally { active--; }
}), _ => Task.FromResult<Credentials?>(credentials)))
{
    await Task.WhenAll(serial.TickAsync(default), serial.TickAsync(default));
    Check(maximum == 1, "cycles concurrents sérialisés");
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    var canceled = false; try { await serial.TickAsync(cancel.Token); } catch (OperationCanceledException) { canceled = true; }
    Check(canceled, "annulation propagée pour arrêt propre");
}
using (var transport = new LcuTransport())
{
    var blocked = false; try { await transport.GetAsync(credentials, "https://example.com", default); } catch (ArgumentException) { blocked = true; }
    Check(blocked, "transport refuse un endpoint arbitraire avant accès réseau");
}
var folder = Path.Combine(Path.GetTempPath(), "RiftChecks-" + Guid.NewGuid().ToString("N"));
var db = Path.Combine(folder, "settings.db");
try
{
    var store = new SettingsStore(db); store.Initialize();
    Check(store.PreferredRole == "jungle", "SQLite : rôle par défaut");
    store.Set("preferredRole", "utility"); store.Set("lolDirectory", @"D:\Jeux avec espaces\LoL");
    var reopened = new SettingsStore(db); reopened.Initialize();
    Check(reopened.PreferredRole == "utility" && reopened.Get("lolDirectory") == @"D:\Jeux avec espaces\LoL", "SQLite : persistance après réouverture");
    store.Set("key'); DROP TABLE settings; --", "O'Brien");
    Check(store.Get("key'); DROP TABLE settings; --") == "O'Brien", "SQLite : paramètres SQL");
    store.Set("preferredRole", "bad"); Check(store.PreferredRole == "jungle", "SQLite : préférence invalide normalisée");
    using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString()))
    { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version=99"; cmd.ExecuteNonQuery(); }
    var rejected = false; try { store.Initialize(); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "SQLite : schéma futur protégé");
}
finally { if (File.Exists(db)) File.Delete(db); if (Directory.Exists(folder)) Directory.Delete(folder); }
await ProfileChecks.Run(Check);
await AssetChecks.Run(Check);
await PerformanceChecks.Run(Check);
await PersonalProfileChecks.Run(Check);
checkPreferences();
void checkPreferences()
{
    Check(DisplayNumbers.Exact(1234567) == "1.234.567" && DisplayNumbers.Compact(12345) == "12,3 k" && DisplayNumbers.Compact(1250000) == "1,3 M", "affichage : séparateurs et abréviations des grands nombres");
    Check(DisplayNumbers.Compact(999) == "999" && DisplayNumbers.Compact(999999) == "1 M", "affichage : seuils k/M sans 1000 k");
    var directory = Path.Combine(Path.GetTempPath(), "RiftHistory-" + Guid.NewGuid().ToString("N"));
    try
    {
        var history = new ProfileHistory(directory);
        history.Remember("Rayz#EUW", "euw1"); history.Remember("Other#NA", "na1"); history.Remember("Rayz#EUW", "euw1");
        var entries = new ProfileHistory(directory).Read();
        Check(entries.Count == 2 && ProfileHistory.Suggest(entries,"ray").Single().RiotId == "Rayz#EUW", "suggestions : historique persistant dédupliqué et filtrage sans casse");
        for (int i = 0; i < 25; i++) history.Remember($"Test{i}#EUW", "euw1");
        Check(history.Read().Count == 20 && ProfileHistory.Suggest(history.Read(), "Test").Count == 6, "suggestions : historique et résultats bornés");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory,true); }
}
Console.WriteLine($"{passed} contrôles réussis.");

sealed class FakeTransport(Func<Credentials, string, CancellationToken, Task<JsonElement?>> get) : ILcuTransport
{
    public Task<JsonElement?> GetAsync(Credentials c, string endpoint, CancellationToken token) => get(c, endpoint, token);
    public void Dispose() { }
}
