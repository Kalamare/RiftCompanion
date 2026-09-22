namespace Rift.Core;

public sealed record OpggChampion(int Id, string Name, int Games, int Wins, long Kills, long Deaths, long Assists)
{
    public long? Cs { get; init; }
    public long? Seconds { get; init; }
    public double Kda => (double)(Kills + Assists) / Math.Max(1, Deaths);
    public double WinRate => 100.0 * Wins / Games;
    public double AverageKills => (double)Kills / Games;
    public double AverageDeaths => (double)Deaths / Games;
    public double AverageAssists => (double)Assists / Games;
    public string Summary => $"{Games} parties · {WinRate:F1} % victoires · {AverageKills:F1}/{AverageDeaths:F1}/{AverageAssists:F1}";
}
public sealed record OpggProfile(string RiotId, string Platform, DateTimeOffset FetchedAt, DateTimeOffset? UpdatedAt,
    int? Rank, int? Total, string Queue, int? Season, OpggChampion[] Champions)
{
    public int? SeasonGames { get; init; }
    public int? SeasonWins { get; init; }
    public int? SeasonLosses { get; init; }
    public bool HasCompleteChampions => SeasonGames is > 0 && Champions.Sum(c => (long)c.Games) == SeasonGames && Champions.Select(c => c.Id).Distinct().Count() == Champions.Length;
    public string RankLabel => Rank is > 0 && Total >= Rank ? $"Rang serveur {Platform.ToUpperInvariant()} · #{Rank:N0} · Top {100.0 * Rank / Total:F2} %" : "Rang serveur indisponible";
    public string Scope => $"Saison {Season?.ToString() ?? "non précisée"} · {Queue switch { "RANKED" => "Classé · bilan disponible", "SOLORANKED" => "Solo/Duo", "FLEXRANKED" => "Flex", "" => "file non précisée", _ => Queue }}";
    public string DateLabel => $"Récupéré le {FetchedAt.ToLocalTime():dd/MM HH:mm} · profil source {(UpdatedAt is { } date ? date.ToLocalTime().ToString("dd/MM HH:mm") : "non daté")}";
}
