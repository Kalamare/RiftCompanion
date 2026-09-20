using Rift.Core;

namespace Rift.Desktop;

// One detail model for condensed statistics in both the profile and scoreboard.
public sealed record MatchStatistics(ProfileMatch Match)
{
    private string PerMinute(double value) => Match.Seconds > 0 ? (value * 60 / Match.Seconds).ToString("F1") : "—";
    public string Farming => $"Minions et monstres tués : {DisplayNumbers.Exact(Match.Cs)}\nCS / min : {PerMinute(Match.Cs)}\nOr gagné : {DisplayNumbers.Exact(Match.Gold)}\nOr / min : {PerMinute(Match.Gold)}";
    public string Combat => (Match.Participation is { } kp ? $"{kp:F0} % de participation aux éliminations" : "Participation : indisponible") +
        $"\nDégâts aux champions : {DisplayNumbers.Exact(Match.Damage)}\nDégâts / min : {PerMinute(Match.Damage)}";
    public string Vision => $"Score de vision : {DisplayNumbers.Exact(Match.Vision)}\nVision / min : {PerMinute(Match.Vision)}\nBalises posées : {Match.WardsPlaced}\nBalises détruites : {Match.WardsKilled}\nBalises de contrôle achetées : {Match.ControlWards}";
}
