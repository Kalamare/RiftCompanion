using System.Text.Json;
using Rift.Core;

namespace Rift.Infrastructure;

public sealed partial class RiotProfileClient
{
    public async Task<SeasonHistory> LoadSeasonAsync(string platform, string puuid, string key, DateTimeOffset from,
        ProfileCache cache, SeasonHistoryStore store, IProgress<SeasonHistory>? progress, CancellationToken token)
    {
        if (!Platforms.TryGetValue(platform, out var routing) || string.IsNullOrWhiteSpace(puuid) || puuid.Length > 256 ||
            from >= DateTimeOffset.UtcNow || from.Year < 2021) throw new ArgumentException("Période ou joueur invalide.");
        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl)) throw new ArgumentException("Renseigne une clé Riot valide dans les paramètres de connexion.");
        cache.Initialize();
        var saved = store.Read(platform, puuid, from);
        var state = saved is { Exhausted: false } ? saved : new SeasonHistory(platform, puuid, from,
            DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()), 0, false, 0, saved?.Matches ?? []);
        var rows = state.Matches.ToDictionary(m => m.Id);
        using var operation = RuntimeDiagnostics.Begin("Statistiques", "Synchronisation de la période");
        while (!state.Exhausted && state.NextStart < 10000)
        {
            token.ThrowIfCancellationRequested();
            var rawIds = await Get(routing.Region, $"/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(puuid)}/ids?start={state.NextStart}&count=100&startTime={from.ToUnixTimeSeconds()}&endTime={state.Until.ToUnixTimeSeconds()}", key, token);
            if (rawIds is not { ValueKind: JsonValueKind.Array }) throw new RiotApiException("Liste des parties indisponible. La synchronisation peut être reprise.");
            var ids = rawIds.Value.EnumerateArray().Select(x => x.GetString() ?? throw new JsonException()).ToArray();
            int missing = state.Missing, count = 0;
            foreach (var id in ids.Distinct())
            {
                token.ThrowIfCancellationRequested();
                var cacheKey = $"v2:{routing.Region}:{puuid}:{id}";
                var match = rows.GetValueOrDefault(id) ?? cache.Get(cacheKey);
                if (match is null)
                {
                    var raw = await Get(routing.Region, $"/lol/match/v5/matches/{Uri.EscapeDataString(id)}", key, token);
                    match = raw.HasValue ? ProfileParser.Match(raw.Value, puuid) : null;
                    if (match is not null && match.Id == id) cache.Set(cacheKey, match);
                }
                if (match is not null && match.Id == id) rows[id] = match; else missing++;
                // A cancelled page is replayed; successful details are reused from cache.
                if (++count % 20 == 0) progress?.Report(state with { Matches = rows.Values.ToArray() });
            }
            state = state with { NextStart = state.NextStart + ids.Length, Exhausted = ids.Length < 100,
                Missing = missing, Matches = rows.Values.OrderByDescending(m => m.PlayedAt).ToArray() };
            store.Save(state); progress?.Report(state);
        }
        return state;
    }
}
