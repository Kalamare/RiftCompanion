using System.Net;
using System.Net.Http.Json;
using Rift.Client;
using Rift.Contracts;

var url = new Uri(Environment.GetEnvironmentVariable("RIFT_SERVER_URL") ?? "http://127.0.0.1:5080/");
var keyFile = Environment.GetEnvironmentVariable("RIFT_SERVER_ACCESS_KEY_FILE") ?? throw new InvalidOperationException("Configure RIFT_SERVER_ACCESS_KEY_FILE.");
var key = File.ReadAllText(keyFile).Trim();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); var token = timeout.Token;
using var http = new HttpClient { BaseAddress = url, Timeout = TimeSpan.FromSeconds(10) };
var health = await http.GetFromJsonAsync<Dictionary<string, string>>("health", token);
if (health?.GetValueOrDefault("data") != "synthetic") throw new InvalidOperationException("These checks only run against explicit synthetic demo data.");
using (var unauthorized = await http.GetAsync("v1/players/euw1/fixture-player-0", token))
    if (unauthorized.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("Unauthenticated request accepted");
http.DefaultRequestHeaders.Add("X-Rift-Key", key);
using (var invalid = await http.PostAsJsonAsync("v1/profiles/lookup", new ProfileLookup("bad", "invalid"), token))
    if (invalid.StatusCode != HttpStatusCode.BadRequest) throw new Exception("Invalid lookup accepted");
Console.WriteLine("PASS: API authentication and input validation");
await using var client = new RiftServerClient(url, key);
// Subscribe before first lookup. Repeatable with an existing demo: notifications tested via a second fresh
// private server instance or the next scheduled refresh (see Check.ps1, which uses an isolated project).
var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
client.Changed += () => changed.TrySetResult();
await client.Watch("euw1", "fixture-player-0", token);
var profile = await client.Load("Alice#TEST", "euw1", token);
await changed.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
StatsSnapshot stats;
do { stats = await client.Stats("euw1", profile.Puuid, from, 0, token); if (stats.Summary.Games == 0) await Task.Delay(500, token); }
while (stats.Summary.Games == 0);
if (stats.Summary.Games != 1 || stats.Champions.Length != 1 || stats.Summary.CsPerMinute != 6) throw new Exception("Server aggregate mismatch");
profile = await client.Profile("euw1", profile.Puuid, 0, 0, token);
if (profile.Matches.Count != 1 || profile.HasMore || profile.Level != 100) throw new Exception("Server profile/page mismatch");
var details = await client.Details("euw1", profile.Matches[0].Id, token);
if (details.Participants.Count != 10) throw new Exception("Missing participants");
if (await client.Ranks("euw1", "fixture-player-1", token) is not null) throw new Exception("Invented incidental rank");
if ((await client.Ranks("euw1", "fixture-player-0", token))?.Entries.Count != 1) throw new Exception("Missing refreshed rank");
await using (var reconnected = new RiftServerClient(url, key))
{
    await reconnected.Watch("euw1", profile.Puuid, token);
    if ((await reconnected.Stats("euw1", profile.Puuid, from, 0, token)).Summary != stats.Summary) throw new Exception("Reconnect lost persisted state");
}
Console.WriteLine("PASS: real REST client, SignalR notification, season stats, profile, details, unknown rank and reconnect reconciliation");
var refresh = await client.Refresh("euw1", profile.Puuid, true, token);
var repeated = await client.Refresh("euw1", profile.Puuid, true, token);
if (repeated.Accepted || repeated.Status is not ("pending" or "cooldown")) throw new Exception("Repeated refresh accepted");
var status = await client.Refresh("euw1", profile.Puuid, false, token);
if (status.ServerTime == default || status.Status is not ("pending" or "cooldown")) throw new Exception("Missing refresh state");
Console.WriteLine("PASS: real refresh endpoint enforces shared pending/cooldown state");
