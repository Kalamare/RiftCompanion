using Rift.Core;
namespace Rift.Desktop;
public sealed record ProfileMatchRow(ProfileMatch Match, ProfileVisual ChampionVisual, IReadOnlyList<ProfileVisual> Items)
{
    public string Result => Match.Result;
    public string QueueLabel => Match.QueueLabel;
    public string KdaLine => Match.KdaLine;
    public string Cs => DisplayNumbers.Exact(Match.Cs);
    public string Damage => DisplayNumbers.Compact(Match.Damage);
    public string DamageExact => DisplayNumbers.Exact(Match.Damage);
    public string Vision => DisplayNumbers.Exact(Match.Vision);
    public string PlayedLabel => Match.PlayedLabel;
}
