using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Rift.Core;

namespace Rift.Infrastructure;

// Deliberately not a record: default ToString must not expose the password.
public sealed class Credentials(int port, string password)
{
    public int Port { get; } = port;
    internal string Password { get; } = password;
    internal bool SameAs(Credentials? other) => other is not null && Port == other.Port && Password == other.Password;
    public static Credentials Parse(string text)
    {
        var fields = text.Trim().Split(':');
        if (fields.Length != 5 || fields[0] != "LeagueClient" || !int.TryParse(fields[1], out var pid) || pid <= 0 ||
            !int.TryParse(fields[2], out var port) || port is < 1 or > 65535 || fields[3].Length == 0 || fields[4] != "https")
            throw new FormatException("Fichier de connexion invalide.");
        return new Credentials(port, fields[3]);
    }
}

public static class LockfileDiscovery
{
    public static async Task<Credentials?> FindAsync(string? directory, CancellationToken token)
    {
        var roots = new[] { directory, Environment.GetEnvironmentVariable("LOL_DIRECTORY"), @"C:\Riot Games\League of Legends", @"D:\Riot Games\League of Legends" };
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
        {
            try
            {
                await using var stream = new FileStream(Path.Combine(root!, "lockfile"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 4096) throw new FormatException("Fichier de connexion invalide.");
                using var reader = new StreamReader(stream);
                return Credentials.Parse(await reader.ReadToEndAsync(token));
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return null;
    }
}

public interface ILcuTransport : IDisposable
{
    Task<JsonElement?> GetAsync(Credentials credentials, string endpoint, CancellationToken token);
}

public sealed class LcuTransport : ILcuTransport
{
    private static readonly HashSet<string> Allowed = ["/lol-gameflow/v1/gameflow-phase", "/lol-champ-select/v1/session", "/lol-game-data/assets/v1/champion-summary.json", "/lol-summoner/v1/current-summoner", "/riotclient/region-locale"];
    private HttpClient? client;
    private Credentials? current;
    public async Task<JsonElement?> GetAsync(Credentials credentials, string endpoint, CancellationToken token)
    {
        if (!Allowed.Contains(endpoint)) throw new ArgumentException("Endpoint non autorisé.");
        if (!credentials.SameAs(current))
        {
            client?.Dispose();
            var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            // LCU has a self-signed certificate. Exception is scoped to this fixed loopback origin only.
            handler.ServerCertificateCustomValidationCallback = (request, _, _, _) => request.RequestUri is { Scheme: "https", Host: "127.0.0.1" } uri && uri.Port == credentials.Port;
            client = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{credentials.Port}"), Timeout = TimeSpan.FromMilliseconds(1500), MaxResponseContentBufferSize = 4_000_000 };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{credentials.Password}")));
            current = credentials;
        }
        using var response = await client!.GetAsync(endpoint, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return json.RootElement.Clone();
    }
    public void Dispose() { client?.Dispose(); current = null; }
}
