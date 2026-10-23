using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rift.Contracts;
using Rift.Core;

namespace Rift.Desktop;

public partial class ProfileView
{
    private StatsSnapshot? sidebarSeason;
    private int sidebarQueue = -1;
    private int sidebarLimit = 6;
    private string? sidebarError;
    private void OnSeasonMode(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var queue)) return;
        sidebarQueue = queue; sidebarLimit = 6; sidebarError = null;
        Interlocked.Exchange(ref remoteDirty, 1); RenderSidebarSeason();
    }
    private void OnMoreSeasonChampions(object sender, RoutedEventArgs e)
    { sidebarLimit += 6; RenderSidebarSeason(); }
    private async Task LoadSidebarSeason(PlayerProfile selected, long version, CancellationToken token)
    {
        int queue = sidebarQueue;
        if (sidebarSeason is { } saved && saved.Platform == selected.Platform && saved.Puuid == selected.Puuid && saved.Queue == queue && saved.Version == version && saved.From == StatisticsFrom) return;
        try
        {
            var result = await remote!.Stats(selected.Platform, selected.Puuid, StatisticsFrom, queue, token);
            token.ThrowIfCancellationRequested();
            if (profile?.Puuid != selected.Puuid || profile.Platform != selected.Platform || sidebarQueue != queue) return;
            sidebarSeason = result; sidebarError = null;
            if (assets is not null)
            {
                var images = await Task.Run(() => result.Champions.Select(c => (c.ChampionId, c.Champion,
                    Name: assets.ChampionName(c.ChampionId, c.Champion), Icon: bitmaps.Get(assets.ChampionImage(c.ChampionId, c.Champion), 120))).ToArray(), token);
                token.ThrowIfCancellationRequested();
                if (profile?.Puuid != selected.Puuid || profile.Platform != selected.Platform) return;
                foreach (var c in images) Visual(c.ChampionId, c.Champion, true).Update(c.Name, c.Icon);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException || ex is OperationCanceledException && !token.IsCancellationRequested)
        { sidebarError = "Statistiques de saison momentanément indisponibles"; }
    }
    private void RenderSidebarSeason()
    {
        foreach (var button in SidebarModeButtons.Children.OfType<Button>())
        {
            bool active = button.Tag?.ToString() == sidebarQueue.ToString();
            button.Background = (Brush)new BrushConverter().ConvertFromString(active ? "#29423D" : "#262626")!;
            button.Foreground = active ? Brushes.Turquoise : Brushes.LightSteelBlue;
        }
        var data = sidebarSeason is { } s && profile is { } p && s.Puuid == p.Puuid && s.Platform == p.Platform && s.Queue == sidebarQueue ? s : null;
        string queue = sidebarQueue == 420 ? "Solo" : sidebarQueue == 440 ? "Flex" : "Solo + Flex";
        SidebarSeasonScope.Text = data is null ? sidebarError ?? $"{queue} · saison depuis le {StatisticsFrom:dd/MM/yyyy} · en attente du serveur" :
            $"{queue} · depuis le {data.From:dd/MM/yyyy} · {data.Summary.Games} parties enregistrées\n" +
            (data.Coverage.AccessibleHistoryScanned && data.Coverage.MissingDetails == 0 ? "Historique accessible parcouru" : "Historique partiel · collecte de saison non forcée");
        SidebarChampions.ItemsSource = data?.Champions.OrderByDescending(c => c.Summary.Games).ThenBy(c => c.ChampionId).Take(sidebarLimit).Select(c => new
        {
            Visual = Visual(c.ChampionId, c.Champion, true), Kda = $"{c.Summary.Kda:F2} KDA", WinRate = $"{c.Summary.WinRate:F0} %",
            Games = $"{c.Summary.Games} parties", Detail = $"{c.Summary.Wins} V / {c.Summary.Games - c.Summary.Wins} D · {c.Summary.Kills:F1} / {c.Summary.Deaths:F1} / {c.Summary.Assists:F1}"
        }).ToArray();
        MoreSeasonChampions.Visibility = data?.Champions.Length > sidebarLimit ? Visibility.Visible : Visibility.Collapsed;
    }
}
