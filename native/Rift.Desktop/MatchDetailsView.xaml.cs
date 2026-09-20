using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;
using Rift.Core;

namespace Rift.Desktop;

public partial class MatchDetailsView : UserControl
{
    private readonly System.Windows.Threading.DispatcherTimer runeLeaveTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private System.Windows.Window? runeOwner;
    public MatchDetailsView()
    {
        InitializeComponent();
        runeLeaveTimer.Tick += (_, _) => { if (RuneScroll.IsMouseCaptureWithin) return; runeLeaveTimer.Stop(); if (RunesPopup.PlacementTarget?.IsMouseOver != true && !RunesPopup.Child.IsMouseOver) RunesPopup.IsOpen = false; };
        Loaded += (_, _) => { runeOwner = System.Windows.Window.GetWindow(this); if (runeOwner is not null) runeOwner.Deactivated += OnOwnerDeactivated; };
        Unloaded += (_, _) => { runeLeaveTimer.Stop(); RunesPopup.IsOpen = false; if (runeOwner is not null) runeOwner.Deactivated -= OnOwnerDeactivated; runeOwner = null; };
        PreviewMouseDown += (_, _) => { if (RunesPopup.IsOpen && RunesPopup.PlacementTarget?.IsMouseOver != true && !RunesPopup.Child.IsMouseOver) RunesPopup.IsOpen = false; };
    }
    private void OnOwnerDeactivated(object? sender, EventArgs e) => RunesPopup.IsOpen = false;
    private void OnRunesLeave(object sender, System.Windows.Input.MouseEventArgs e) { runeLeaveTimer.Stop(); runeLeaveTimer.Start(); }
    private void OnRunePopupEnter(object sender, System.Windows.Input.MouseEventArgs e) => runeLeaveTimer.Stop();
    private void OpenRunes(object sender)
    {
        if (sender is not Button { DataContext: DetailPlayerRow row } button) return;
        runeLeaveTimer.Stop();
        RuneScroll.MaxHeight = Math.Min(540, Math.Max(180, System.Windows.SystemParameters.WorkArea.Height * .65 - 80));
        RunesPopup.DataContext = row; RunesPopup.PlacementTarget = button; RunesPopup.IsOpen = true;
    }
    private void OnRunes(object sender, System.Windows.RoutedEventArgs e) => OpenRunes(sender);
    private void OnRunesHover(object sender, System.Windows.Input.MouseEventArgs e) => OpenRunes(sender);
    private void OnRuneKey(object sender, System.Windows.Input.KeyEventArgs e)
    { if (e.Key == System.Windows.Input.Key.Escape) { RunesPopup.IsOpen = false; e.Handled = true; } }
    private void OnRuneWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    { RuneScroll.ScrollToVerticalOffset(RuneScroll.VerticalOffset - e.Delta); e.Handled = true; }
    public event Action<MatchParticipant>? PlayerRequested;
    private void OnPlayerProfile(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DetailPlayerRow row } && row.CanOpenProfile) PlayerRequested?.Invoke(row.Player);
    }
}

internal sealed class MatchDetailsPresentation
{
    public string Heading { get; }
    public string Subtitle { get; }
    public IReadOnlyList<DetailTeamRow> Teams { get; }
    public IReadOnlyList<ProfileVisual> Visuals { get; }
    public ProfileMatch[] AssetMatches { get; }
    public int[] SpellIds { get; }
    public int Queue { get; }
    public MatchDetailsPresentation(MatchDetails details, string selectedPuuid, bool demo)
    {
        Queue = details.Queue;
        Heading = QueueCatalog.Label(details.Queue);
        Subtitle = $"{details.PlayedAt.ToLocalTime():dd MMMM yyyy · HH:mm}  ·  {(int)(details.Seconds / 60)}:{(int)(details.Seconds % 60):00}" + (demo ? "  ·  Démonstration fictive" : "");
        var visuals = new Dictionary<string, ProfileVisual>();
        ProfileVisual Visual(int id, string name, bool champion)
        {
            var key = $"{champion}:{id}:{name}";
            if (!visuals.TryGetValue(key, out var visual)) visuals[key] = visual = new(id, name, champion);
            return visual;
        }
        ProfileVisual Spell(int id)
        {
            var key = $"spell:{id}";
            if (!visuals.TryGetValue(key, out var visual))
            {
                visuals[key] = visual = new(id, "", false) { IsSpell = true };
                visual.Update("Sort non disponible", null);
            }
            return visual;
        }
        ProfileVisual Rune(int id)
        {
            var key = $"rune:{id}";
            if (!visuals.TryGetValue(key, out var visual))
            {
                visuals[key] = visual = new(id, "", false) { IsRune = true };
                visual.Update("Rune non disponible", null);
            }
            return visual;
        }
        ProfileVisual Quest(int? id)
        {
            var key = $"quest:{id}";
            if (!visuals.TryGetValue(key, out var visual))
            {
                visuals[key] = visual = new(id ?? 0, "", false) { IsRoleQuest = true };
                visual.Update(id is null ? "Quête de rôle non fournie" : id == 0 ? "Aucun objet de quête de rôle" : "Quête de rôle", null);
            }
            return visual;
        }
        var roles = new[] { "top", "jungle", "middle", "bottom", "utility", "" };
        var teams = details.Teams.OrderByDescending(t => details.Participants.Any(p => p.TeamId == t.Id && p.Puuid == selectedPuuid)).ThenBy(t => t.Id);
        Teams = teams.Select(team =>
        {
            var players = details.Participants.Where(p => p.TeamId == team.Id).OrderBy(p => Array.IndexOf(roles, p.Stats.Role)).ToArray();
            var rows = players.Select(p => new DetailPlayerRow(p, p.Puuid == selectedPuuid, Visual(p.Stats.ChampionId, p.Stats.Champion, true),
                Enumerable.Range(0, 7).Select(i => Visual(p.Stats.Items.ElementAtOrDefault(i), "", false)).ToArray(),
                Enumerable.Range(0, 2).Select(i => Spell(p.Spells.ElementAtOrDefault(i))).ToArray(),
                new[] { p.Runes.Keystone }.Where(id => id > 0).Select(Rune).ToArray(),
                p.Runes.Selections.Select(Rune).ToArray(), Quest(p.RoleBoundItem))).ToArray();
            string[] labels = ["Tours", "Dragons", "Barons", "Hérauts", "Inhibiteurs", "Larves"];
            string[] keys = ["tower", "dragon", "baron", "riftHerald", "inhibitor", "horde"];
            var goals = keys.Select((key, i) => team.Objectives.TryGetValue(key, out var value)
                ? new DetailObjective(labels[i], value, ProfileIcons.Create(key, labels[i]).Source) : null).OfType<DetailObjective>().ToArray();
            var title = players.Any(p => p.Stats.Remake) ? "Remake" : team.Win switch { true => "Victoire", false => "Défaite", _ => $"Équipe {team.Id}" };
            return new DetailTeamRow(title, team.Win == true ? "#64D8B8" : team.Win == false ? "#E990A0" : "#9FB3C1",
                $"{players.Sum(p => p.Stats.Kills)} / {players.Sum(p => p.Stats.Deaths)} / {players.Sum(p => p.Stats.Assists)}",
                goals, rows, team.Bans.Where(id => id > 0).Select(id => Visual(id, "Champion banni", true)).ToArray(),
                team.Bans.Length == 0 ? "Bans : non fournis pour cette partie" : $"Bans{(team.Bans.Any(id => id <= 0) ? " · certains joueurs n’ont pas banni" : "")}");
        }).ToArray();
        Visuals = visuals.Values.ToArray();
        SpellIds = details.Participants.SelectMany(p => p.Spells).Distinct().ToArray();
        var seed = details.Participants[0].Stats;
        AssetMatches = details.Participants.Select(p => p.Stats with { Items = p.Stats.Items.Concat(p.RoleBoundItem is > 0 ? new[] { p.RoleBoundItem.Value } : []).ToArray() }).Concat(details.Teams.SelectMany(t => t.Bans).Where(id => id > 0)
            .Select(id => seed with { ChampionId = id, Champion = "", Items = [] })).ToArray();
    }
}
internal sealed record DetailObjective(string Label, int Count, ImageSource Icon);
internal sealed record DetailTeamRow(string Title, string Accent, string Score, IReadOnlyList<DetailObjective> Objectives,
    IReadOnlyList<DetailPlayerRow> Players, IReadOnlyList<ProfileVisual> Bans, string BansLabel);
internal sealed record DetailPlayerRow(MatchParticipant Player, bool IsSelectedPlayer, ProfileVisual Champion, IReadOnlyList<ProfileVisual> Items,
    IReadOnlyList<ProfileVisual> Spells, IReadOnlyList<ProfileVisual> Runes, IReadOnlyList<ProfileVisual> FullRunes, ProfileVisual RoleQuest) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string ShortRank { get; private set; } = "Rang…";
    public IReadOnlyList<ProfileVisual> EquipmentItems => Items.Take(6).ToArray();
    public ProfileVisual Trinket => Items[6];
    public IReadOnlyList<ProfileVisual> PrimaryRunes => Player.Runes.PrimarySelections.Length > 0
        ? FullRunes.Where(r => Player.Runes.PrimarySelections.Contains(r.Id)).ToArray() : FullRunes;
    public IReadOnlyList<ProfileVisual> SecondaryRunes => FullRunes.Where(r => Player.Runes.SecondarySelections.Contains(r.Id)).ToArray();
    public MatchStatistics Statistics { get; } = new(Player.Stats);
    public bool CanOpenProfile => !string.IsNullOrWhiteSpace(Player.Puuid);
    public ProfileVisual Rank { get; } = MakeRank();
    private static ProfileVisual MakeRank() { var rank = new ProfileVisual(0, "", true); rank.Update("Rang : chargement…", null); return rank; }
    public void SetRank(PlayerRanks? ranks, int queue, ImageSource? image, bool demo = false)
    {
        var kind = queue == 440 ? "RANKED_FLEX_SR" : "RANKED_SOLO_5x5";
        var label = queue == 440 ? "Flex" : "Solo/Duo";
        var entry = ranks?.Entries.FirstOrDefault(r => r.Queue == kind);
        var tier = entry?.Tier switch { "IRON" => "Fer", "BRONZE" => "Bronze", "SILVER" => "Argent", "GOLD" => "Or", "PLATINUM" => "Platine", "EMERALD" => "Émeraude", "DIAMOND" => "Diamant", "MASTER" => "Maître", "GRANDMASTER" => "Grand maître", "CHALLENGER" => "Challenger", _ => entry?.Tier };
        var value = ranks is null ? "indisponible" : entry is null ? "Non classé" : $"{tier} {entry.Division} · {entry.Lp} LP";
        ShortRank = value; PropertyChanged?.Invoke(this, new(nameof(ShortRank)));
        Rank.Update($"{label} : {value}" + (demo ? " · fictif" : ranks is null ? "" : $" · {ranks.FetchedAt.ToLocalTime():dd/MM HH:mm}"), image);
    }
    public string RiotId => Player.RiotId;
    public string Level => Player.Level > 0 ? $"Niv. {Player.Level}" : "Niv. —";
    public string Role => Player.Stats.Role.Length > 0 ? Player.Stats.RoleLabel : "";
    public string Kda => Player.Stats.KdaLine;
    public string Farm => $"{Player.Stats.Cs} CS · {DisplayNumbers.Compact(Player.Stats.Gold)} or";
    public string Metrics => $"{DisplayNumbers.Compact(Player.Stats.Damage)} dégâts · Vision {Player.Stats.Vision} · " +
        (Player.Stats.Participation is { } kp ? $"{kp:F0} % participation" : "Participation —");
    public string ParticipationLine => Player.Stats.Participation is { } kp ? $"{kp:F0} % participation" : "Participation —";
    public string VisionLine => $"Vision : {Player.Stats.Vision}";
}
