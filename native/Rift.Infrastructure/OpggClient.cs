using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rift.Core;

namespace Rift.Infrastructure;

public sealed class OpggClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset retryAfter;
    private static readonly string[] Fields = ["data.summoner.{game_name,tagline,region,updated_at}", "data.summoner.ladder_rank.{rank,total}", "data.summoner.most_champions.{game_type,season_id,year,play,win,lose}", "data.summoner.most_champions.champion_stats[].{id,champion_name,play,win,lose,kill,death,assist,minion_kill,neutral_minion_kill,game_length_second}"];
    public OpggClient(string dataDirectory, HttpMessageHandler? handler = null)
    {
        directory = Path.Combine(dataDirectory, "opgg-cache-v2");
        http = new(new DiagnosticHttpHandler("Profils externes", handler ?? new HttpClientHandler { AllowAutoRedirect = false })) { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 1_000_000 };
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json"); http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
    }
    public static string Region(string platform) => platform.ToLowerInvariant() switch
    {
        "euw1" => "EUW", "eun1" => "EUNE", "na1" => "NA", "kr" => "KR", "jp1" => "JP", "br1" => "BR", "la1" => "LAN", "la2" => "LAS", "oc1" => "OCE", "tr1" => "TR", "ru" => "RU", "sg2" => "SG", "tw2" => "TW", "vn2" => "VN", "me1" => "ME", _ => throw new ArgumentException("Serveur OP.GG non pris en charge")
    };
    public async Task<OpggProfile?> LoadAsync(string riotId, string platform, CancellationToken token)
    {
        int split = riotId.LastIndexOf('#'); if (split <= 0 || split == riotId.Length - 1 || riotId.EndsWith("#BOT", StringComparison.OrdinalIgnoreCase)) return null;
        string region = Region(platform);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(platform + ":" + riotId.ToUpperInvariant())));
            string path = Path.Combine(directory, key + ".json"); OpggProfile? cached = null;
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length <= 1_100_000)
                {
                    var envelope = JsonSerializer.Deserialize<Cache>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
                    if (envelope is not null) cached = OpggPayload.Parse(envelope.Text, riotId, platform, envelope.FetchedAt);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or OverflowException) { }
            var now = DateTimeOffset.UtcNow;
            if (cached is not null && now >= cached.FetchedAt && now - cached.FetchedAt < TimeSpan.FromHours(1)) return cached;
            if (now < retryAfter) return cached;
            try
            {
                using var response = await http.PostAsJsonAsync("https://mcp-api.op.gg/mcp", new
                {
                    jsonrpc = "2.0", id = 1, method = "tools/call",
                    @params = new { name = "lol_get_summoner_profile", arguments = new { game_name = riotId[..split], tag_line = riotId[(split + 1)..], region, desired_output_fields = Fields } }
                }, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    retryAfter = response.Headers.RetryAfter?.Date ?? now + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(2));
                    if (retryAfter <= now) retryAfter = now.AddMinutes(2);
                    return cached;
                }
                response.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                if (!body.RootElement.TryGetProperty("result", out var result) || result.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True) throw new JsonException("OP.GG unavailable");
                string text = result.GetProperty("content").EnumerateArray().First(c => c.GetProperty("type").GetString() == "text").GetProperty("text").GetString()!;
                var parsed = OpggPayload.Parse(text, riotId, platform, now);
                try
                {
                    Directory.CreateDirectory(directory);
                    // Bounded persistent cache; stale entries remain useful offline.
                    foreach (var old in new DirectoryInfo(directory).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(199)) old.Delete();
                    await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new Cache(now, text)), token).ConfigureAwait(false);
                    File.Move(path + ".tmp", path, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                return parsed;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or OperationCanceledException or OverflowException)
            { retryAfter = DateTimeOffset.UtcNow.AddMinutes(1); return cached; }
        }
        finally { gate.Release(); }
    }
    private sealed record Cache(DateTimeOffset FetchedAt, string Text);
    public void Dispose() => http.Dispose();
}
