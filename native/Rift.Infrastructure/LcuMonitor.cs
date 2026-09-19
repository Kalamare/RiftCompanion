using System.Diagnostics;
using System.Text.Json;
using Rift.Core;

namespace Rift.Infrastructure;

public sealed class LcuMonitor(ILcuTransport transport, Func<CancellationToken, Task<Credentials?>> discover) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Credentials? identity;
    private int catalogAttempts;
    private bool catalogLoaded;
    private long requests;
    private long errors;
    private long latency = -1;
    public long Requests => Interlocked.Read(ref requests);
    public long Errors => Interlocked.Read(ref errors);
    public long LatencyMs => Interlocked.Read(ref latency);
    public ClientState State { get; private set; } = ClientState.Offline();
    public IReadOnlyDictionary<int, string> Champions { get; private set; } = new Dictionary<int, string>();
    public int IntervalMs { get; private set; } = 5000;

    private async Task<JsonElement?> Get(Credentials c, string endpoint, CancellationToken token)
    {
        Interlocked.Increment(ref requests); var start = Stopwatch.GetTimestamp();
        try { return await transport.GetAsync(c, endpoint, token); }
        finally { Interlocked.Exchange(ref latency, (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
    }
    public async Task<ClientState> TickAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var c = await discover(token);
            if (c is null) { identity = null; catalogLoaded = false; catalogAttempts = 0; IntervalMs = 5000; return State = ClientState.Offline(); }
            if (!c.SameAs(identity)) { identity = c; catalogLoaded = false; catalogAttempts = 0; Champions = new Dictionary<int, string>(); }
            var phaseJson = await Get(c, "/lol-gameflow/v1/gameflow-phase", token);
            if (phaseJson is not { ValueKind: JsonValueKind.String }) throw new JsonException();
            var phase = phaseJson.Value.GetString()!;
            Draft? draft = null;
            if (phase == "ChampSelect")
            {
                var session = await Get(c, "/lol-champ-select/v1/session", token);
                if (session.HasValue) draft = DraftParser.Parse(session.Value);
            }
            State = new ClientState(true, phase, draft, phase == "ChampSelect" && draft is null ? "Draft temporairement indisponible." : "Lecture seule · synchronisation automatique");
            IntervalMs = phase == "ChampSelect" ? 2000 : phase is "InProgress" or "Reconnect" ? 15000 : 5000;
            if (!catalogLoaded && catalogAttempts < 2 && phase is not ("InProgress" or "Reconnect"))
            {
                catalogAttempts++;
                try
                {
                    var data = await Get(c, "/lol-game-data/assets/v1/champion-summary.json", token);
                    if (data is { ValueKind: JsonValueKind.Array })
                    {
                        Champions = data.Value.EnumerateArray().Where(x => DraftParser.Integer(x, "id") > 0)
                            .GroupBy(x => DraftParser.Integer(x, "id")).ToDictionary(g => g.Key, g => DraftParser.Text(g.First(), "name"));
                        catalogLoaded = true;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { }
            }
            return State;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or HttpRequestException or TaskCanceledException or JsonException or ArgumentException)
        {
            Interlocked.Increment(ref errors); IntervalMs = 5000;
            return State = ClientState.Offline("Connexion indisponible. Nouvelle tentative automatique.");
        }
        finally { gate.Release(); }
    }
    public void Dispose() { transport.Dispose(); gate.Dispose(); }
}
