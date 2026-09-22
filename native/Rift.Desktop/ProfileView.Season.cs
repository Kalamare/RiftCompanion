using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Rift.Core;
using Rift.Infrastructure;

namespace Rift.Desktop;

public partial class ProfileView
{
    private OpggProfile? seasonProfile;
    private TextBlock SeasonText(string text, double size = 12, Brush? brush = null) => new()
    { Text = text, FontSize = size, Foreground = brush ?? Brushes.LightSteelBlue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };

    private void RenderRankCards()
    {
        RankPanel.Children.Clear(); RankLinks.Children.Clear();
        if (profile is null) return;
        foreach (var queue in new[] { ("RANKED_SOLO_5x5", "Soloqueue"), ("RANKED_FLEX_SR", "Classé Flex") })
        {
            var rank = profile.Ranks.FirstOrDefault(r => r.Queue == queue.Item1);
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            if (rank is not null && ranks.GetValueOrDefault(rank.Tier) is { } emblem)
                grid.Children.Add(new Image { Source = emblem, Width = 112, Height = 112, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
            var detail = new StackPanel(); Grid.SetColumn(detail, 1); grid.Children.Add(detail);
            detail.Children.Add(new TextBlock { Text = rank is null ? "Non classé" : $"{TierLabel(rank.Tier)} {rank.Division} {rank.Lp} LP", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            detail.Children.Add(SeasonText(queue.Item2, 14));
            bool solo = queue.Item1 == "RANKED_SOLO_5x5";
            var regional = solo && seasonProfile?.Rank is > 0 ? $"{RiotProfileClient.Platforms[profile.Platform].Label} : {seasonProfile.Rank:N0}" : "serveur : —";
            var ranking = SeasonText($"Rang mondial : —  ·  {regional}");
            ranking.ToolTip = solo ? "Position régionale, fournie au niveau du profil sans file distincte. Le rang mondial n’est pas disponible." : "Classement régional Flex distinct et mondial non disponibles.";
            detail.Children.Add(ranking);
            detail.Children.Add(SeasonText(solo && seasonProfile is { Rank: > 0, Total: > 0 } source ? $"Top {100.0 * source.Rank / source.Total:F2} %" : "Top : —"));
            if (rank is not null)
            {
                var score = new TextBlock { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
                score.Inlines.Add(new Run("Victoires : ") { Foreground = Brushes.LightSteelBlue });
                score.Inlines.Add(new Run(rank.Wins.ToString()) { Foreground = new SolidColorBrush(Color.FromRgb(50, 217, 160)) });
                score.Inlines.Add(new Run("  ·  Défaites : ") { Foreground = Brushes.LightSteelBlue });
                score.Inlines.Add(new Run(rank.Losses.ToString()) { Foreground = new SolidColorBrush(Color.FromRgb(255, 112, 123)) });
                detail.Children.Add(score);
            }
            if (solo)
            {
                var date = SeasonText(seasonProfile?.DateLabel ?? "Rang serveur en attente ou indisponible", 10);
                detail.Children.Add(date); RankPanel.Children.Add(grid);
            }
            else RankPanel.Children.Add(new Expander { Header = "Classement Flex", Content = grid, Foreground = Brushes.LightSteelBlue });
        }
    }

    private void RenderSeason()
    {
        if (remote is not null && profile is { Demo: false }) { RenderRemoteSeason(); return; }
        MetricsPanel.Children.Clear(); ExtraMetricsPanel.Children.Clear(); RolePanel.Children.Clear(); ChampionsGrid.ItemsSource = null;
        var value = seasonProfile;
        var history = seasonHistory is { } saved && saved.Puuid == profile?.Puuid && saved.Platform == profile.Platform && saved.From == StatisticsFrom ? saved : null;
        AddStatisticsModes(history);
        int Mode(ComboBox box) => box.SelectedValue is int id ? id : -1;
        bool MatchesSource(int mode) => value is not null && (mode == -1 && value.Queue == "RANKED" || mode == 420 && value.Queue == "SOLORANKED" || mode == 440 && value.Queue == "FLEXRANKED");
        void Metric(Panel target, string title, string number, string? reason = null)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 8, 10, 10), ToolTip = reason };
            cell.Children.Add(SeasonText(title)); cell.Children.Add(SeasonText(number, 22, Brushes.White)); target.Children.Add(cell);
        }
        var mode = Mode(SummaryMode);
        if (history is not null)
        {
            var stats = ProfileSummary.From(history.Select(mode));
            SeasonScope.Text = history.Coverage;
            SampleText.Text = $"{stats.Games} parties · {stats.Wins} victoires · {stats.Games - stats.Wins} défaites";
            string Number(double n, string format = "F1") => stats.Games > 0 ? n.ToString(format) : "—";
            Metric(MetricsPanel, "Victoires", stats.Games > 0 ? $"{stats.WinRate:F1} %" : "—");
            Metric(MetricsPanel, "KDA global", Number(stats.Kda, "F2"), "(Total kills + assists) / total morts ; diviseur minimal de 1.");
            Metric(MetricsPanel, "Participation kills", stats.Participation is { } kp ? $"{kp:F1} %" : "—", "Moyenne par partie de (kills + assists) / kills de l’équipe. Parties sans kill d’équipe exclues.");
            Metric(MetricsPanel, "CS / min", Number(stats.CsPerMinute), "Total sbires et monstres tués / durée totale en minutes.");
            Metric(ExtraMetricsPanel, "Kills moyens", Number(stats.Kills));
            Metric(ExtraMetricsPanel, "Morts moyennes", Number(stats.Deaths));
            Metric(ExtraMetricsPanel, "Assists moyennes", Number(stats.Assists));
            Metric(ExtraMetricsPanel, "Dégâts / min", Number(stats.DamagePerMinute));
            Metric(ExtraMetricsPanel, "Or / min", Number(stats.GoldPerMinute));
            Metric(ExtraMetricsPanel, "Vision moyenne", Number(stats.Vision));
        }
        else
        {
            var source = MatchesSource(mode) ? value : null;
            SeasonScope.Text = source?.Scope ?? "Synchronise la période pour calculer ce mode.";
            SampleText.Text = source?.SeasonGames is { } games ? $"{games} parties · {source.SeasonWins} victoires · {source.SeasonLosses} défaites" : "Bilan indisponible pour ce mode";
            Metric(MetricsPanel, "Victoires", source?.SeasonGames is > 0 ? $"{100.0 * source.SeasonWins / source.SeasonGames:F1} %" : "—");
            Metric(MetricsPanel, "KDA global", source is { HasCompleteChampions: true } ? $"{source.Champions.Sum(c => c.Kills + c.Assists) / (double)Math.Max(1, source.Champions.Sum(c => c.Deaths)):F2}" : "—");
            Metric(MetricsPanel, "Participation kills", "—", "Disponible après synchronisation des détails des parties.");
            var csComplete = source is { HasCompleteChampions: true } && source.Champions.All(c => c.Cs is not null && c.Seconds is > 0);
            Metric(MetricsPanel, "CS / min", csComplete ? $"{60.0 * source!.Champions.Sum(c => c.Cs!.Value) / source.Champions.Sum(c => c.Seconds!.Value):F1}" : "—");
            ExtraMetricsPanel.Children.Add(SeasonText("Synchronise la période pour calculer les statistiques manquantes.", 11));
        }
        RolesScope.Text = history?.Coverage ?? "Synchronise la période pour calculer les rôles.";
        var roleRows = history?.Select(Mode(RolesMode)).Where(m => QueueCatalog.HasStandardRoles(m.Queue)).ToArray() ?? [];
        foreach (var group in roleRows.GroupBy(m => m.RoleLabel).OrderByDescending(g => g.Count()))
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition());
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(ProfileIcons.Create(group.First().Role, group.Key, 24));
            name.Children.Add(SeasonText(group.Key)); row.Children.Add(name);
            void Bar(int column, double percent, string label, Brush color)
            {
                var cell = new StackPanel { Margin = new Thickness(7, 0, 0, 0) };
                var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = percent, Height = 6, Foreground = color };
                cell.Children.Add(bar); cell.Children.Add(SeasonText(label, 11)); Grid.SetColumn(cell, column); row.Children.Add(cell);
            }
            Bar(1, 100.0 * group.Count() / roleRows.Length, $"{group.Count()} parties", Brushes.DeepSkyBlue);
            Bar(2, 100.0 * group.Count(m => m.Win) / group.Count(), $"{100.0 * group.Count(m => m.Win) / group.Count():F1} % V", Brushes.Turquoise);
            RolePanel.Children.Add(row);
        }
        if (roleRows.Length == 0) RolePanel.Children.Add(SeasonText(history is null ? "Répartition disponible après synchronisation." : "Aucune partie avec rôle standard pour ce mode."));
        if (history is not null)
        {
            ChampionsScope.Text = history.Coverage;
            ChampionsGrid.ItemsSource = history.Select(Mode(ChampionsMode)).GroupBy(m => m.ChampionId > 0 ? m.ChampionId.ToString() : m.Champion).OrderByDescending(g => g.Count()).Select(g =>
            {
                var stats = ProfileSummary.From(g); var m = g.First();
                return new { ChampionVisual = Visual(m.ChampionId, m.Champion, true), Games = stats.Games, WinRate = $"{stats.WinRate:F0} %", Kda = $"{stats.Kda:F2}",
                    SeasonDetail = $"{stats.Kills:F1} / {stats.Deaths:F1} / {stats.Assists:F1} · CS {g.Average(x => x.Cs):F0} ({stats.CsPerMinute:F1}/min)" };
            }).ToArray();
        }
        else
        {
            var source = MatchesSource(Mode(ChampionsMode)) ? value : null;
            ChampionsScope.Text = source is null ? "Synchronise la période pour calculer ce mode." : $"{source.Scope} · {source.Champions.Length} champions disponibles · liste potentiellement partielle";
            ChampionsGrid.ItemsSource = source?.Champions.OrderByDescending(c => c.Games).Select(c => new
            {
                ChampionVisual = Visual(c.Id, c.Name, true), Games = c.Games, WinRate = $"{c.WinRate:F0} %", Kda = $"{c.Kda:F2}",
                SeasonDetail = $"{c.AverageKills:F1} / {c.AverageDeaths:F1} / {c.AverageAssists:F1}" +
                    (c.Cs is { } cs && c.Seconds is > 0 ? $" · CS {cs / (double)c.Games:F0} ({60.0 * cs / c.Seconds:F1}/min)" : "")
            }).ToArray();
        }
        ChampionsScope.ToolTip = history?.Coverage ?? value?.DateLabel;
    }
}
