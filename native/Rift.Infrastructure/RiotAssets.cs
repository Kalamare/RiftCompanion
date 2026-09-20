using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rift.Core;

namespace Rift.Infrastructure;

public sealed record RiotAsset(int Id, string Code, string Name, string File)
{
    public string Description { get; init; } = "";
    public int? Price { get; init; }
    public int[] Components { get; init; } = [];
}

// Public Data Dragon requests have no API key. Only requested icons are downloaded.
public sealed class RiotAssets(string directory, HttpMessageHandler? handler = null, string? bundledDirectory = null) : IDisposable
{
    private readonly HttpClient http = new(new DiagnosticHttpHandler("CDN", handler ?? new HttpClientHandler { AllowAutoRedirect = false }))
        { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 8_000_000 };
    private readonly Dictionary<int, RiotAsset> champions = [];
    private readonly Dictionary<int, RiotAsset> items = [];
    private readonly Dictionary<int, RiotAsset> spells = [];
    private readonly Dictionary<int, RiotAsset> bundled = ReadBundled(bundledDirectory);
    private readonly Dictionary<int, RiotAsset> bundledSpells = ReadBundled(bundledDirectory, "summoner-fr.json");
    private readonly Dictionary<int, RiotAsset> runes = ReadRunes(bundledDirectory);
    private static Dictionary<int, RiotAsset> ReadRunes(string? folder)
    {
        try { return folder is null ? [] : ParseCatalog(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(folder, "..", "Runes", "runes-fr.json"))), false).ToDictionary(x => x.Id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    public string RuneName(int id) => runes.GetValueOrDefault(id)?.Name ?? "Rune non disponible";
    public string RuneDescription(int id) => runes.GetValueOrDefault(id)?.Description ?? "Description indisponible.";
    public string SpellDescription(int id) => (spells.GetValueOrDefault(id) ?? bundledSpells.GetValueOrDefault(id))?.Description ?? "Description indisponible.";
    public string ItemDescription(int id) => items.GetValueOrDefault(id)?.Description ?? "";
    public RiotAsset? Item(int id) => items.GetValueOrDefault(id);
    public string? RuneImage(int id)
    {
        if (bundledDirectory is null || !runes.TryGetValue(id, out var rune)) return null;
        var path = Path.Combine(bundledDirectory, "..", "Runes", rune.File);
        return File.Exists(path) ? path : null;
    }
    private readonly ConcurrentDictionary<string, string> images = new();
    public string Version { get; private set; } = "";
    public string Notice { get; private set; } = "";
    public RiotAsset? Champion(int id, string code) => champions.GetValueOrDefault(id) ?? bundled.GetValueOrDefault(id) ?? champions.Values.Concat(bundled.Values).FirstOrDefault(x => x.Code == code || x.Name == code);
    public string ChampionName(int id, string code) => Champion(id, code)?.Name ?? (code == "MonkeyKing" ? "Wukong" : code);
    public string ItemName(int id) => id == 0 ? "Emplacement vide" : items.GetValueOrDefault(id)?.Name ?? $"Objet {id} (catalogue indisponible)";
    public string? ChampionImage(int id, string code) => BundledImage(Champion(id, code)) ?? Image("champion", Champion(id, code));
    public string? ItemImage(int id) => Image("item", items.GetValueOrDefault(id));
    public string SpellName(int id) => (spells.GetValueOrDefault(id) ?? bundledSpells.GetValueOrDefault(id))?.Name ?? "Sort non disponible";
    public string? SpellImage(int id) => BundledImage(bundledSpells.GetValueOrDefault(id)) ?? Image("spell", spells.GetValueOrDefault(id));
    private string? BundledImage(RiotAsset? asset)
    {
        if (asset is null || bundledDirectory is null) return null;
        var path = Path.Combine(bundledDirectory, asset.File);
        return File.Exists(path) ? path : null;
    }
    private static Dictionary<int, RiotAsset> ReadBundled(string? folder, string file = "champion-fr.json")
    {
        try { return folder is null ? [] : ParseCatalog(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(folder, file))), true).ToDictionary(x => x.Id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    public string? ProfileIconImage(int? id) => id is >= 0 ? images.GetValueOrDefault($"{Version}/profileicon/{id}.png") : null;
    private string? Image(string type, RiotAsset? asset) => asset is null ? null : images.GetValueOrDefault($"{Version}/{type}/{asset.File}");
    public static IReadOnlyList<RiotAsset> ParseCatalog(JsonElement root, bool champion)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) throw new JsonException();
        var entries = new List<RiotAsset>();
        foreach (var entry in data.EnumerateObject())
        {
            var value = entry.Value;
            if (!int.TryParse(champion ? DraftParser.Text(value, "key") : entry.Name, out var id) || id <= 0) continue;
            var name = DraftParser.Text(value, "name");
            var file = value.TryGetProperty("image", out var img) ? DraftParser.Text(img, "full") : "";
            if (name.Length == 0 || !Regex.IsMatch(file, @"\A[A-Za-z0-9_-]+\.png\z")) continue;
            entries.Add(new(id, champion ? DraftParser.Text(value, "id") : entry.Name, name, file)
                {
                    Description = PlainDescription(DraftParser.Text(value, "description")),
                    Price = value.TryGetProperty("gold", out var gold) && gold.ValueKind == JsonValueKind.Object &&
                        gold.TryGetProperty("total", out var price) && price.ValueKind == JsonValueKind.Number && price.TryGetInt32(out var amount) && amount >= 0 ? amount : null,
                    Components = value.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Array
                        ? from.EnumerateArray().Select(x => int.TryParse(x.ToString(), out var component) ? component : 0).Where(x => x > 0).ToArray() : []
                });
        }
        return entries;
    }
    public static string PlainDescription(string html)
    {
        var lines = Regex.Replace(html, @"<br\s*/?>|</(?:p|li)>", "\n", RegexOptions.IgnoreCase);
        var plain = System.Net.WebUtility.HtmlDecode(Regex.Replace(lines, "<[^>]*>", "")).Trim();
        plain = Regex.Replace(plain, @"\n(?:[^\S\n]*\n){2,}", "\n\n");
        // Some current Riot descriptions contain unresolved client-only parameters.
        return Regex.Replace(plain, @"@[A-Za-z0-9_.]+@", "valeur indisponible");
    }
    private async Task<JsonElement> JsonFile(string relative, string local, bool refresh, CancellationToken token, bool offlineOnly = false)
    {
        if (File.Exists(local) && (offlineOnly || !refresh || File.GetLastWriteTimeUtc(local) > DateTime.UtcNow.AddHours(-24)))
        {
            try { return JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(local, token)); }
            catch (JsonException) { } // Replace damaged cache from the official source.
        }
        if (offlineOnly) throw new IOException("Catalogue absent du cache.");
        try
        {
            var json = await http.GetStringAsync("https://ddragon.leagueoflegends.com/" + relative, token);
            var parsed = JsonSerializer.Deserialize<JsonElement>(json);
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            await File.WriteAllTextAsync(local + ".tmp", json, token); File.Move(local + ".tmp", local, true);
            return parsed;
        }
        catch (HttpRequestException) when (File.Exists(local)) { return JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(local, token)); }
    }
    public async Task PrepareAsync(IEnumerable<ProfileMatch> matches, CancellationToken token, Func<Task>? changed = null, bool offlineOnly = false, int? profileIconId = null, IEnumerable<int>? spellIds = null)
    {
        Notice = "";
        try
        {
            Directory.CreateDirectory(directory);
            var versions = await JsonFile("api/versions.json", Path.Combine(directory, "versions.json"), true, token, offlineOnly);
            var version = versions.ValueKind == JsonValueKind.Array && versions.GetArrayLength() > 0 ? versions[0].GetString() ?? "" : "";
            if (!Regex.IsMatch(version, @"\A\d+\.\d+\.\d+\z")) throw new JsonException();
            var folder = Path.Combine(directory, version);
            if (Version != version || champions.Count == 0 || items.Count == 0)
            {
                var c = ParseCatalog(await JsonFile($"cdn/{version}/data/fr_FR/champion.json", Path.Combine(folder, "champion-fr.json"), false, token, offlineOnly), true);
                var i = ParseCatalog(await JsonFile($"cdn/{version}/data/fr_FR/item.json", Path.Combine(folder, "item-fr.json"), false, token, offlineOnly), false);
                champions.Clear(); items.Clear(); spells.Clear(); images.Clear();
                foreach (var asset in c) champions[asset.Id] = asset;
                foreach (var asset in i) items[asset.Id] = asset;
                Version = version;
            }
            var rows = matches.ToArray();
            var requestedSpells = (spellIds ?? []).Where(id => id > 0).Distinct().ToArray();
            if (requestedSpells.Any(id => !bundledSpells.ContainsKey(id)) && spells.Count == 0)
            {
                try
                {
                    foreach (var spell in ParseCatalog(await JsonFile($"cdn/{version}/data/fr_FR/summoner.json", Path.Combine(folder, "summoner-fr.json"), false, token, offlineOnly), true)) spells[spell.Id] = spell;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException || ex is OperationCanceledException && !token.IsCancellationRequested) { }
            }
            var needed = rows.Select(m => (Type: "champion", Asset: Champion(m.ChampionId, m.Champion)))
                .Where(x => BundledImage(x.Asset) is null)
                .Concat(requestedSpells.Where(id => !bundledSpells.ContainsKey(id)).Select(id => (Type: "spell", Asset: spells.GetValueOrDefault(id))))
                .Concat(rows.SelectMany(m => m.Items).Where(id => id > 0).SelectMany(id => new[] { id }.Concat(items.GetValueOrDefault(id)?.Components ?? []))
                    .Select(id => (Type: "item", Asset: items.GetValueOrDefault(id))))
                .Concat(profileIconId is >= 0 ? new[] { (Type: "profileicon", Asset: (RiotAsset?)new RiotAsset(profileIconId.Value, "", "Icône de profil", $"{profileIconId.Value}.png")) } : [])
                .Where(x => x.Asset is not null).Distinct().ToArray();
            // Expose all cached icons before starting network work for missing files.
            foreach (var entry in needed)
            {
                var path = Path.Combine(folder, entry.Type, entry.Asset!.File);
                if (File.Exists(path)) images[$"{version}/{entry.Type}/{entry.Asset.File}"] = path;
            }
            if (changed is not null) await changed();
            if (offlineOnly) { Notice = "Illustrations enregistrées sur cet ordinateur."; return; }
            int missing = 0;
            await Parallel.ForEachAsync(needed.Where(entry => !images.ContainsKey($"{version}/{entry.Type}/{entry.Asset!.File}")), new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = token }, async (entry, ct) =>
            {
                var key = $"{version}/{entry.Type}/{entry.Asset!.File}";
                var path = Path.Combine(folder, entry.Type, entry.Asset.File);
                try
                {
                    if (!File.Exists(path))
                    {
                        var bytes = await http.GetByteArrayAsync($"https://ddragon.leagueoflegends.com/cdn/{key.Insert(version.Length + 1, "img/")}", ct);
                        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) throw new IOException("Invalid image");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllBytesAsync(path + ".tmp", bytes, ct); File.Move(path + ".tmp", path, true);
                    }
                    images[key] = path;
                    if (changed is not null) await changed();
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException || ex is OperationCanceledException && !token.IsCancellationRequested)
                { Interlocked.Increment(ref missing); }
            });
            Notice = $"Illustrations : catalogue Riot {Version} (actuel, pas le patch historique)." + (missing > 0 ? " Certaines icônes sont indisponibles." : "");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException || ex is OperationCanceledException && !token.IsCancellationRequested)
        { Notice = "Catalogue Riot indisponible : statistiques conservées, illustrations éventuellement manquantes."; }
    }
    public void Dispose() => http.Dispose();
}
