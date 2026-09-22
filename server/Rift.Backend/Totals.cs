using Rift.Core;

namespace Rift.Backend;

public sealed record Totals(int Queue, int ChampionId, string Champion, string Role, long Games, long Wins,
    long Kills, long Deaths, long Assists, long Cs, long Damage, long Gold, long Vision, long Wards,
    long WardsKilled, long ControlWards, double Seconds, double KpSum, long KpCount)
{
    public static ProfileSummary Summary(IEnumerable<Totals> input)
    {
        var rows = input.ToArray(); var games = rows.Sum(x => x.Games); var seconds = rows.Sum(x => x.Seconds); var kpCount = rows.Sum(x => x.KpCount);
        double Mean(Func<Totals, long> get) => games == 0 ? 0 : rows.Sum(get) / (double)games;
        double PerMinute(Func<Totals, long> get) => seconds == 0 ? 0 : 60.0 * rows.Sum(get) / seconds;
        return new(checked((int)games), checked((int)rows.Sum(x => x.Wins)),
            (rows.Sum(x => x.Kills) + rows.Sum(x => x.Assists)) / (double)Math.Max(1, rows.Sum(x => x.Deaths)),
            PerMinute(x => x.Cs), PerMinute(x => x.Damage), PerMinute(x => x.Gold), Mean(x => x.Vision),
            kpCount == 0 ? null : rows.Sum(x => x.KpSum) / kpCount,
            Mean(x => x.Kills), Mean(x => x.Deaths), Mean(x => x.Assists), Mean(x => x.Wards), Mean(x => x.WardsKilled), Mean(x => x.ControlWards));
    }
}
