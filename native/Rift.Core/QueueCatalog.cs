using System.Text.Json;

namespace Rift.Core;

public static class QueueCatalog
{
    public sealed record ClientQueue(int Id, string Name, string Description, string Group, string Category);
    private static IReadOnlyDictionary<int, ClientQueue> clientQueues = ReadClient();
    private static IReadOnlyDictionary<int, ClientQueue> ReadClient()
    {
        using var stream = typeof(QueueCatalog).Assembly.GetManifestResourceStream("Rift.Core.Data.client-queues-fr.json")!;
        using var document = JsonDocument.Parse(stream);
        return ParseClient(document.RootElement);
    }
    public static IReadOnlyDictionary<int, ClientQueue> ParseClient(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid queue catalog");
        var result = new Dictionary<int, ClientQueue>();
        foreach (var row in root.EnumerateArray())
        {
            if (!row.TryGetProperty("id", out var id) || !id.TryGetInt32(out int value) || value < 0) continue;
            var name = DraftParser.Text(row, "name");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200) continue;
            result[value] = new(value, name, DraftParser.Text(row, "description"), DraftParser.Text(row, "gameSelectModeGroup"), DraftParser.Text(row, "gameSelectCategory"));
        }
        if (result.Count == 0) throw new JsonException("Empty queue catalog");
        return result;
    }
    public static void UpdateClient(JsonElement root) => Interlocked.Exchange(ref clientQueues, ParseClient(root));
    // Official queues.json snapshot; 710 is the Ranked 5v5 queue observed in the client.
    private static readonly IReadOnlyDictionary<int, (string Map, string Description)> Queues = Read();
    private static IReadOnlyDictionary<int, (string, string)> Read()
    {
        using var stream = typeof(QueueCatalog).Assembly.GetManifestResourceStream("Rift.Core.Data.queues.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("queueId").GetInt32(),
            x => (x.GetProperty("map").GetString() ?? "", x.GetProperty("description").GetString() ?? "Partie personnalisée"));
    }
    public static bool HasStandardRoles(int queue) => clientQueues.TryGetValue(queue, out var entry)
        ? entry.Group == "kSummonersRift"
        : queue is 2 or 4 or 6 or 14 or 42 or 61 or 400 or 410 or 420 or 430 or 440 or 480 or 490 or 700 or 710;
    public static string Label(int queue)
    {
        if (clientQueues.TryGetValue(queue, out var entry))
        {

            if (entry.Category is "kCoopVsAI" or "kVersusAI") return $"Coop vs IA · {entry.Description}";
            if (entry.Name.Contains("Classé", StringComparison.OrdinalIgnoreCase)) return entry.Name;
            if (entry.Group == "kSummonersRift" && queue != 700) return entry.Name == "Normal" ? entry.Description : entry.Name;
            return entry.Name;
        }
        return FallbackLabel(queue);
    }
    private static string FallbackLabel(int queue) => queue switch
    {
        0 => "Personnalisée", 400 or 14 => "Draft", 420 or 4 => "Solo/Duo",
        440 => "Flex", 710 or 42 => "5v5", 430 or 2 => "Aveugle",
        480 => "Swiftplay", 490 => "Partie rapide",
        450 or 65 or 100 => "ARAM", 700 => "Tournoi · Clash", 720 => "Tournoi · Clash ARAM",
        1700 or 1710 => "Mode spécial · Arena", 900 => "Mode spécial · ARURF", 1900 => "Mode spécial · URF",
        2000 => "Tutoriel · 1", 2010 => "Tutoriel · 2", 2020 => "Tutoriel · 3",
        _ => Describe(queue)
    };
    private static string Describe(int queue)
    {
        if (!Queues.TryGetValue(queue, out var data)) return $"Mode non répertorié ({queue})";
        return data.Description.Replace(" games", "", StringComparison.OrdinalIgnoreCase);
    }
}
