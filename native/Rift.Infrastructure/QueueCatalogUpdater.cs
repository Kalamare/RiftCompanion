using System.Text.Json;
using Rift.Core;

namespace Rift.Infrastructure;

// Public client data mirror: no Riot credentials are sent. Embedded catalog works offline.
public sealed class QueueCatalogUpdater(string directory, HttpMessageHandler? handler = null) : IDisposable
{
    private readonly HttpClient http = new(new DiagnosticHttpHandler("Catalogue", handler ?? new HttpClientHandler { AllowAutoRedirect = false }))
        { Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 4_000_000 };
    private bool checkedThisSession;
    public async Task PrepareAsync(CancellationToken token)
    {
        if (checkedThisSession) return;
        var path = Path.Combine(directory, "client-queues-fr.json");
        try
        {
            if (File.Exists(path))
            {
                try
                {
                    QueueCatalog.UpdateClient(JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(path, token)));
                    if (File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddHours(-24)) { checkedThisSession = true; return; }
                }
                catch (JsonException) { }
            }
            var json = await http.GetStringAsync("https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/fr_fr/v1/queues.json", token);
            QueueCatalog.UpdateClient(JsonSerializer.Deserialize<JsonElement>(json));
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path + ".tmp", json, token); File.Move(path + ".tmp", path, true);
            checkedThisSession = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException || ex is OperationCanceledException && !token.IsCancellationRequested)
        { checkedThisSession = true; }
    }
    public void Dispose() => http.Dispose();
}
