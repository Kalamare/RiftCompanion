using System.Text.Json;
using Rift.Core;

namespace Rift.Infrastructure;

// Separate from the draft monitor: two local reads, no champion catalog or public API.
public sealed class PersonalProfileDetector(ILcuTransport transport, Func<CancellationToken, Task<Credentials?>> discover) : IDisposable
{
    public async Task<PlayerProfile?> DetectAsync(CancellationToken token)
    {
        try
        {
            var credentials = await discover(token);
            if (credentials is null) return null;
            var summoner = await transport.GetAsync(credentials, "/lol-summoner/v1/current-summoner", token);
            if (summoner is not { ValueKind: JsonValueKind.Object }) return null;
            var region = await transport.GetAsync(credentials, "/riotclient/region-locale", token);
            if (region is not { ValueKind: JsonValueKind.Object }) return null;
            return Parse(summoner.Value, region.Value);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or HttpRequestException or TaskCanceledException or JsonException or ArgumentException)
        { return null; }
    }

    public static PlayerProfile? Parse(JsonElement summoner, JsonElement region)
    {
        var name = DraftParser.Text(summoner, "gameName");
        var tag = DraftParser.Text(summoner, "tagLine");
        // The tag is not a routing region. Never guess the server from it or the locale.
        var platform = DraftParser.Text(region, "region").ToUpperInvariant() switch
        { "EUW" or "EUW1" => "euw1", "EUNE" or "EUN1" => "eun1", "NA" or "NA1" => "na1", "KR" => "kr", _ => "" };
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tag) || platform.Length == 0) return null;
        return new(name + "#" + tag, platform, Math.Max(0, DraftParser.Integer(summoner, "summonerLevel")), [], [], DateTimeOffset.UtcNow, false, 0,
            "Compte détecté dans LoL. Actualise le profil pour charger les statistiques.")
        { ProfileIconId = summoner.TryGetProperty("profileIconId", out var icon) && icon.ValueKind == JsonValueKind.Number && icon.TryGetInt32(out var id) && id >= 0 ? id : null };
    }
    public void Dispose() => transport.Dispose();
}

// Only a locally detected account can establish ownership. Searches never replace it.
public sealed class PersonalProfileStore(string directory)
{
    private readonly string path = Path.Combine(directory, "personal-profile.json");
    public static bool SameAccount(PlayerProfile? a, PlayerProfile? b) => a is not null && b is not null &&
        a.Platform == b.Platform && a.RiotId.Equals(b.RiotId, StringComparison.OrdinalIgnoreCase);
    public PlayerProfile? Read()
    {
        if (!File.Exists(path)) return null;
        try
        {
            var profile = JsonSerializer.Deserialize<PlayerProfile>(File.ReadAllText(path));
            return profile is { Demo: false, Ranks: not null, Matches: not null } &&
                !string.IsNullOrWhiteSpace(profile.RiotId) && profile.RiotId.Contains('#') &&
                profile.Platform is not null && RiotProfileClient.Platforms.ContainsKey(profile.Platform) &&
                profile.Matches.All(m => m is not null && m.Items is not null) && profile.Ranks.All(r => r is not null) ? profile : null;
        }
        catch (JsonException) { return null; }
    }
    public void Save(PlayerProfile profile)
    {
        if (profile.Demo) throw new ArgumentException("Un exemple ne peut pas devenir le profil personnel.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(profile));
        File.Move(path + ".tmp", path, true);
    }
}
