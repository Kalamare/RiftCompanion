using Rift.Core;

namespace Rift.Infrastructure;

public sealed class DiagnosticHttpHandler(string category, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        // Classify locally, retain no URI or player identifier in the diagnostics.
        var path = request.RequestUri?.AbsolutePath ?? "";
        var name = category == "LCU" ? "Lecture client LoL" : path.EndsWith(".png") ? "Image CDN" :
            path.Contains("/account/") ? "Compte Riot" : path.Contains("/summoner/") ? "Niveau du joueur" :
            path.Contains("/league/") ? "Classement" : path.EndsWith("/ids") ? "Liste des parties" :
            path.Contains("/matches/") ? "Détail de partie" : "Catalogue";
        using var operation = RuntimeDiagnostics.Begin(category, name);
        try
        {
            var response = await base.SendAsync(request, token).ConfigureAwait(false);
            operation.HttpStatus((int)response.StatusCode); return response;
        }
        catch (OperationCanceledException) { operation.Cancelled(); throw; }
        catch { operation.Failed(); throw; }
    }
}
