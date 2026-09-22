using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Rift.Core;
using Rift.Infrastructure;

namespace Rift.Desktop;

public partial class ProfileView
{
    private SeasonHistory? seasonHistory;
    private CancellationTokenSource? statsCancellation;
    private Task statsLoading = Task.CompletedTask;
    private Task statsRestoring = Task.CompletedTask;
    private bool statsReady;
    private void AddStatisticsModes(SeasonHistory? value)
    {
        if (value is null || SummaryMode.ItemsSource is not Dictionary<int, string> current) return;
        var missing = value.Matches.Select(m => m.Queue).Distinct().Where(q => !current.ContainsKey(q)).ToArray();
        if (missing.Length == 0) return;
        var modes = new Dictionary<int, string>(current);
        foreach (var queue in missing) modes[queue] = QueueCatalog.Label(queue);
        statsReady = false;
        foreach (var box in new[] { SummaryMode, RolesMode, ChampionsMode })
        { var selected = box.SelectedValue; box.ItemsSource = modes; box.SelectedValue = selected; }
        statsReady = true;
    }
    private DateTimeOffset StatisticsFrom => new(DateTime.SpecifyKind(StatsFrom.SelectedDate ?? DateTime.Today, DateTimeKind.Utc));
    private void InitializeStatistics()
    {
        var modes = new Dictionary<int, string> { [0] = "Tout", [-1] = "Classé", [420] = "Soloqueue", [440] = "Flex",
            [450] = "ARAM", [1700] = "Arène", [400] = "Normal · Draft", [430] = "Normal · Aveugle", [490] = "Partie rapide" };
        foreach (var box in new[] { SummaryMode, RolesMode, ChampionsMode }) { box.ItemsSource = modes; box.SelectedValue = -1; }
        // Date boundary is explicit: no claim that a provider's season ID is a calendar year.
        StatsFrom.SelectedDate = DateTime.Today.Year == 2026 ? new DateTime(2026, 1, 8) : new DateTime(DateTime.Today.Year, 1, 1);
        statsReady = true;
    }
    private void OnStatsFilter(object sender, SelectionChangedEventArgs e)
    { if (statsReady && profile is not null) RenderSeason(); }
    private async void OnStatsPeriod(object? sender, SelectionChangedEventArgs e)
    {
        if (!statsReady || profile is null || !statsLoading.IsCompleted) return;
        if (remote is not null) { RenderRemoteSeason(); return; }
        seasonHistory = null;
        try { statsRestoring = RestoreStatistics(profile, personalLifetime.Token); await statsRestoring; }
        catch (OperationCanceledException) { }
        if (!stopping) RenderSeason();
    }
    private async Task RestoreStatistics(PlayerProfile selected, CancellationToken token)
    {
        if (remote is not null || dataDirectory is null || selected.Demo || selected.Puuid.Length == 0) return;
        var from = StatisticsFrom;
        var saved = await Task.Run(() => new SeasonHistoryStore(dataDirectory).Read(selected.Platform, selected.Puuid, from), token);
        if (token.IsCancellationRequested || stopping || profile?.Puuid != selected.Puuid || profile.Platform != selected.Platform || StatisticsFrom != from) return;
        seasonHistory = saved;
        if (saved is not null) await PrepareHistoryImages(saved, token);
        if (!token.IsCancellationRequested && !stopping && profile?.Puuid == selected.Puuid && profile.Platform == selected.Platform && StatisticsFrom == from) RenderSeason();
    }
    private void OnCancelStats(object sender, RoutedEventArgs e) => statsCancellation?.Cancel();
    private async void OnSyncStats(object sender, RoutedEventArgs e)
    {
        if (remote is not null || profile is null || profile.Demo || cache is null || dataDirectory is null || isPreparing || stopping || !statsLoading.IsCompleted) return;
        var selected = profile; var key = ApiKeyBox.Password; var from = StatisticsFrom;
        statsCancellation?.Dispose(); statsCancellation = CancellationTokenSource.CreateLinkedTokenSource(personalLifetime.Token);
        var token = statsCancellation.Token;
        SyncStats.IsEnabled = StatsFrom.IsEnabled = false; CancelStats.IsEnabled = true;
        StatsProgress.Text = "Synchronisation en cours… Les filtres restent utilisables.";
        bool Current() => !stopping && profile?.Puuid == selected.Puuid && profile.Platform == selected.Platform && StatisticsFrom == from;
        var progress = new Progress<SeasonHistory>(value =>
        {
            if (!Current() || token.IsCancellationRequested) return;
            seasonHistory = value; StatsProgress.Text = value.Coverage; RenderSeason();
        });
        statsLoading = Run();
        await statsLoading;
        async Task Run()
        {
            try
            {
                var result = await Task.Run(() => api.LoadSeasonAsync(selected.Platform, selected.Puuid, key, from, cache,
                    new SeasonHistoryStore(dataDirectory), progress, token), token);
                if (!Current() || token.IsCancellationRequested) return;
                seasonHistory = result; await PrepareHistoryImages(result, token);
                if (!Current() || token.IsCancellationRequested) return;
                StatsProgress.Text = result.Coverage + (result.NextStart >= 10000 && !result.Exhausted ? " · limite atteinte : choisis une période plus courte." : ""); RenderSeason();
            }
            catch (OperationCanceledException) { if (Current()) StatsProgress.Text = "Synchronisation arrêtée. Les pages enregistrées seront réutilisées."; }
            catch (Exception ex) when (ex is RiotApiException or ArgumentException or HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException)
            { if (Current()) StatsProgress.Text = ex is RiotApiException or ArgumentException ? ex.Message : "Synchronisation interrompue. Les données enregistrées sont conservées ; réessaie plus tard."; }
            finally { if (!stopping) { SyncStats.IsEnabled = StatsFrom.IsEnabled = true; CancelStats.IsEnabled = false; } }
        }
    }
    private async Task PrepareHistoryImages(SeasonHistory value, CancellationToken token)
    {
        if (assets is null) return;
        var prepared = await Task.Run(() => value.Matches.DistinctBy(m => m.ChampionId).Select(m => (Match: m,
            Name: assets.ChampionName(m.ChampionId, m.Champion), Icon: bitmaps.Get(assets.ChampionImage(m.ChampionId, m.Champion), 120))).ToArray(), token);
        token.ThrowIfCancellationRequested();
        foreach (var item in prepared) Visual(item.Match.ChampionId, item.Match.Champion, true).Update(item.Name, item.Icon);
    }
}
