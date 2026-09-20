namespace Rift.Core;

public sealed record PerformanceSample(DateTimeOffset At, double CpuPercent, double PrivateMb, double AllocatedMbPerSecond, double UiDelayMs);
public sealed record DiagnosticFinding(string Title, string Evidence, string Advice);

// Heuristics for this desktop app, not proof of a fault. No persistent history or remote analysis.
public sealed class DiagnosticAnalyzer
{
    private readonly Queue<PerformanceSample> history = new();
    public double ObservedSeconds => history.Count < 2 ? 0 : (history.Last().At - history.First().At).TotalSeconds;
    public IReadOnlyList<DiagnosticFinding> Observe(PerformanceSample sample, DiagnosticSnapshot diagnostics)
    {
        // A paused/minimized window does not constitute continuous observation.
        if (history.Count > 0 && (sample.At - history.Last().At).TotalSeconds > 5) history.Clear();
        history.Enqueue(sample);
        while (history.Count > 120 || history.Count > 1 && (sample.At - history.Peek().At).TotalSeconds > 120) history.Dequeue();
        var findings = new List<DiagnosticFinding>();
        var lastTen = history.Where(s => (sample.At - s.At).TotalSeconds <= 10).ToArray();
        bool sustained = lastTen.Length >= 8 && (sample.At - lastTen[0].At).TotalSeconds >= 8;
        if (sustained && lastTen.All(s => s.CpuPercent >= 15))
            findings.Add(new("CPU durablement élevé", $"Moyenne {lastTen.Average(s => s.CpuPercent):F1} % de la capacité totale du PC sur {lastTen.Length} relevés (seuil 15 %).",
                "Comparer les opérations actives, puis vérifier si le CPU redescend une fois les chargements terminés. Le diagnostic lui-même participe au coût."));
        if (sustained && lastTen.All(s => s.AllocatedMbPerSecond >= 50))
            findings.Add(new("Allocations .NET soutenues", $"Moyenne {lastTen.Average(s => s.AllocatedMbPerSecond):F1} Mo/s sur {lastTen.Length} relevés (seuil 50 Mo/s).",
                "Examiner les reconstructions de l’interface et décodages répétés. Les allocations ne sont pas de la mémoire nécessairement conservée."));
        if (lastTen.Count(s => s.UiDelayMs >= 250) >= 3)
            findings.Add(new("Retards répétés de l’interface", $"{lastTen.Count(s => s.UiDelayMs >= 250)} relevés retardés d’au moins 250 ms dans les 10 dernières secondes.",
                "Comparer avec les opérations Interface et Images. Une forte charge globale du PC peut aussi retarder le dispatcher."));
        var minute = history.Where(s => (sample.At - s.At).TotalSeconds <= 65).ToArray();
        if (minute.Length >= 50 && (sample.At - minute[0].At).TotalSeconds >= 55)
        {
            var before = minute.Take(10).Average(s => s.PrivateMb); var after = minute.TakeLast(10).Average(s => s.PrivateMb);
            if (after - before >= 100 && after >= before * 1.2)
                findings.Add(new("Hausse de mémoire à vérifier", $"Mémoire privée moyenne : {before:F0} → {after:F0} Mo en environ une minute (+{after - before:F0} Mo ; seuil +100 Mo et +20 %).",
                    "Vérifier si la mémoire se stabilise après le chargement des images. Un cache qui se remplit ne prouve pas une fuite."));
        }
        var recent = diagnostics.Recent.Where(o => o.Started.AddMilliseconds(o.Milliseconds) >= sample.At.AddSeconds(-30)).ToArray();
        if (recent.Any(o => o.Result == "HTTP 429"))
            findings.Add(new("Quota HTTP atteint", "Une réponse HTTP 429 est présente dans les 30 dernières secondes.", "Laisser expirer Retry-After ; ne pas multiplier les actualisations. Consulter les attentes Quotas."));
        var errors = recent.Count(o => (o.Category is "Riot" or "LCU" or "CDN" or "Catalogue") && (o.Result == "Erreur" || o.Result.StartsWith("HTTP 4") || o.Result.StartsWith("HTTP 5")));
        if (errors >= 3)
            findings.Add(new("Erreurs réseau répétées", $"{errors} erreurs réseau/HTTP dans le journal récent (30 s).", "Lire les codes dans Activité récente : 401/403 pour l’accès, 429 pour le quota, 5xx pour le service. Une LCU fermée peut expliquer ses erreurs."));
        foreach (var operation in diagnostics.Active.Where(o => o.Category == "Chargements" && o.Milliseconds >= 30000).Take(3))
            findings.Add(new("Chargement prolongé", $"{operation.Name} : {operation.Milliseconds / 1000:F0} s (seuil 30 s).", "Consulter les requêtes actives et les attentes Quotas ; une attente réseau n’est pas nécessairement un blocage de l’interface."));
        var slowImages = recent.Count(o => o.Category == "Images" && o.Milliseconds >= 100);
        if (slowImages >= 3)
            findings.Add(new("Décodages d’images lents", $"{slowImages} décodages d’au moins 100 ms dans les 30 dernières secondes.", "Vérifier les dimensions des images, le stockage et les compteurs de cache mémoire."));
        return findings;
    }
}
