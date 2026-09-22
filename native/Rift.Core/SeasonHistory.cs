namespace Rift.Core;

// An independently paginated history: never confused with the visible recent-match sample.
public sealed record SeasonHistory(string Platform, string Puuid, DateTimeOffset From, DateTimeOffset Until,
    int NextStart, bool Exhausted, int Missing, ProfileMatch[] Matches)
{
    public ProfileMatch[] Select(int mode) => Matches.DistinctBy(m => m.Id)
        .Where(m => m.PlayedAt >= From && m.PlayedAt < Until && !m.Remake && m.Seconds > 0 &&
            (mode == 0 || mode == -1 && m.Queue is 420 or 440 || mode == m.Queue)).ToArray();
    public string Coverage => $"Depuis le {From:dd/MM/yyyy} · {Matches.Length} parties analysées · " +
        (Exhausted ? "historique accessible parcouru" : "synchronisation partielle") +
        (Missing > 0 ? $" · {Missing} détails indisponibles" : "") + $" · au {Until.ToLocalTime():dd/MM HH:mm}";
}
