using System.Text.Json;
using Rift.Core;

namespace Rift.Infrastructure;

public sealed class LiveRosterClient : IDisposable
{
    private readonly HttpClient http;
    public LiveRosterClient(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (request, _, _, _) => request.RequestUri is { Scheme: "https", Host: "127.0.0.1", Port: 2999, AbsolutePath: "/liveclientdata/playerlist" } };
        http = new(new DiagnosticHttpHandler("Live", handler)) { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 2_000_000 };
    }
    public async Task<IReadOnlyList<OverlayPlayer>> ReadAsync(CancellationToken token)
    {
        using var response = await http.GetAsync("https://127.0.0.1:2999/liveclientdata/playerlist", token);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return OverlayPlayer.Parse(doc.RootElement);
    }
    public void Dispose() => http.Dispose();
}
