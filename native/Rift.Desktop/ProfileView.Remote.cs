using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rift.Client;
using Rift.Contracts;
using Rift.Core;

namespace Rift.Desktop;

public partial class ProfileView
{
    private RiftServerClient? remote;
    private CancellationTokenSource? remoteCancellation;
    private Task remoteLoading = Task.CompletedTask;
    private int remoteDirty;
    private RefreshStatus? refreshStatus;
    private DateTimeOffset refreshReceivedAt;
    private bool requestingRefresh;
    private async Task RequestRemoteRefresh(PlayerProfile selected)
    {
        if (requestingRefresh) return;
        requestingRefresh = true; RefreshPersonalButton.IsEnabled = false;
        try
        {
            refreshStatus = await remote!.Refresh(selected.Platform, selected.Puuid, true, personalLifetime.Token);
            refreshReceivedAt = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref remoteDirty, 1);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        { ShowNotice("Actualisation indisponible pour le moment. Le profil affiché est conservé."); }
        finally { requestingRefresh = false; RenderRefreshStatus(); }
    }
    private void RenderRefreshStatus()
    {
        if (remote is null || profile is null || isPreparing) return;
        var state = refreshStatus;
        var now = state is null ? DateTimeOffset.UtcNow : state.ServerTime + (DateTimeOffset.UtcNow - refreshReceivedAt);
        var remaining = state?.NextAllowedAt - now;
        bool waiting = state?.Status is "pending" or "deferred" || remaining > TimeSpan.Zero;
        RefreshPersonalButton.IsEnabled = !requestingRefresh && !waiting;
        RefreshPersonalButton.Content = state?.Status == "pending" ? "Actualisation en cours" :
            state?.Status == "deferred" ? "Mise à jour différée" : remaining > TimeSpan.Zero ? $"Actualiser · {(int)remaining.Value.TotalMinutes:00}:{remaining.Value.Seconds:00}" : "Actualiser";
        RefreshPersonalButton.ToolTip = state?.LastUpdatedAt is { } at ? $"Dernière vérification des parties récentes : {at.ToLocalTime():dd/MM HH:mm}" : "Les données disponibles restent consultables pendant la collecte.";
    }

    private void ConfigureRemote()
    {
        remote = RiftServerClient.FromEnvironment();
        if (remote is null) return;
        RiotConnection.Visibility = Visibility.Collapsed;
        SyncStats.Visibility = CancelStats.Visibility = Visibility.Collapsed;
        StatisticsSettings.Header = "Période des statistiques";
        StatisticsSettings.Visibility = Visibility.Collapsed;
        SummaryHeading.Text = "Bilan des parties affichées";
        RolesHeading.Text = "Rôles joués · échantillon";
        ChampionsHeading.Text = "Champions joués · échantillon";
        foreach (var box in new[] { SummaryMode, RolesMode, ChampionsMode }) box.SelectedValue = 0;
        StatsProgress.Text = "Mise à jour automatique par le serveur.";
        remote.Changed += () => Interlocked.Exchange(ref remoteDirty, 1);
    }
    private void StartRemote(PlayerProfile selected)
    {
        remoteCancellation?.Cancel();
        var previous = remoteLoading; var oldToken = remoteCancellation;
        remoteCancellation = CancellationTokenSource.CreateLinkedTokenSource(personalLifetime.Token);
        var token = remoteCancellation.Token;
        refreshStatus = null;

        Interlocked.Exchange(ref remoteDirty, 1);
        remoteLoading = Run();
        async Task Run()
        {
            await previous; oldToken?.Dispose();
            var nextReconcile = DateTimeOffset.MinValue;
            long version = -1;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!isPreparing && IsVisible && (Volatile.Read(ref remoteDirty) == 1 || DateTimeOffset.UtcNow >= nextReconcile))
                    {
                        Interlocked.Exchange(ref remoteDirty, 0);
                        nextReconcile = DateTimeOffset.UtcNow.AddMinutes(1);
                        try
                        {
                            // Subscribe before GET so changes during the snapshot trigger another reconciliation.
                            try { await remote!.Watch(selected.Platform, selected.Puuid, token); }
                            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException || ex is OperationCanceledException && !token.IsCancellationRequested)
                            { /* REST reconciliation remains available if push transport is unavailable. */ }
                            refreshStatus = await remote!.Refresh(selected.Platform, selected.Puuid, false, token);
                            refreshReceivedAt = DateTimeOffset.UtcNow;
                            var card = await remote!.Card(selected.Platform, selected.Puuid, token);
                            token.ThrowIfCancellationRequested();
                            if (profile?.Puuid != selected.Puuid || profile.Platform != selected.Platform) continue;
                            var newest = card.Version;
                            if (newest != version)
                            {
                                var latest = await remote!.Profile(selected.Platform, selected.Puuid, 0, 0, token);
                                token.ThrowIfCancellationRequested();
                                // Keep an expanded history; reuse its match rows and image cache.
                                var merged = latest.Matches.Concat(profile.Matches).DistinctBy(m => m.Id).OrderByDescending(m => m.PlayedAt)
                                    .Take(Math.Max(20, profile.Matches.Count)).ToArray();
                                profile = latest with { Matches = merged, NextStart = merged.Length, HasMore = latest.HasMore || profile.HasMore };
                                UpdateQueueFilters(); Render();
                                MoreButton.Visibility = profile.HasMore ? Visibility.Visible : Visibility.Collapsed;
                                await LoadAssets(token);
                                version = newest; Render();
                            }
                            token.ThrowIfCancellationRequested();
                            RenderRemoteSeason();
                            StatsProgress.Text = "Mise à jour automatique par le serveur.";
                        }
                        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or System.IO.IOException || ex is OperationCanceledException && !token.IsCancellationRequested)
                        {
                            StatsProgress.Text = "Serveur momentanément indisponible · données affichées conservées.";
                            nextReconcile = DateTimeOffset.UtcNow.AddSeconds(30);
                        }
                    }
                    RenderRefreshStatus();
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }
    private void RenderRemoteSeason()
    {
        MetricsPanel.Children.Clear(); ExtraMetricsPanel.Children.Clear(); RolePanel.Children.Clear(); ChampionsGrid.ItemsSource = null;
        StatsSnapshot? Selected(ComboBox box)
        {
            if (profile is null) return null;
            var mode = box.SelectedValue is int id ? id : 0;
            var visibleQueue = QueueBox.SelectedValue is int q ? q : 0;
            var matches = profile.Matches.Where(m => (visibleQueue == 0 || m.Queue == visibleQueue) &&
                (mode == 0 || mode == -1 && m.Queue is 420 or 440 || m.Queue == mode) && !m.Remake && m.Seconds > 0).DistinctBy(m => m.Id).ToArray();
            return new(profile.Platform, profile.Puuid, DateTimeOffset.MinValue, DateTimeOffset.UtcNow, mode, ProfileSummary.From(matches),
                matches.GroupBy(m => m.ChampionId).OrderByDescending(g => g.Count()).Select(g => new ChampionStats(g.Key, g.First().Champion, ProfileSummary.From(g.ToArray()))).ToArray(),
                matches.Where(m => QueueCatalog.HasStandardRoles(m.Queue)).GroupBy(m => m.Role).OrderByDescending(g => g.Count()).Select(g => new RoleStats(g.Key, ProfileSummary.From(g.ToArray()))).ToArray(),
                0, new(null, null, false, 0, "displayed"));
        }
        static string Coverage(StatsSnapshot? value) => value is null ? "Aucune partie chargée" :
            $"Échantillon : {value.Summary.Games} parties affichées du filtre · hors remakes · pas un bilan de saison";
        var summary = Selected(SummaryMode);
        SeasonScope.Text = Coverage(summary); SampleText.Text = summary is null ? "" : $"{summary.Summary.Games} parties · {summary.Summary.Wins} victoires · {summary.Summary.Games - summary.Summary.Wins} défaites";
        if (summary is not null)
        {
            var s = summary.Summary;
            void Metric(Panel panel, string label, double? number, string format = "F1", string suffix = "")
            {
                var cell = new StackPanel { Margin = new Thickness(0, 8, 10, 10) };
                cell.Children.Add(SeasonText(label));
                cell.Children.Add(SeasonText(s.Games == 0 || number is null ? "—" : number.Value.ToString(format) + suffix, 22, Brushes.White));
                panel.Children.Add(cell);
            }
            Metric(MetricsPanel, "Victoires", s.WinRate, suffix: " %"); Metric(MetricsPanel, "KDA global", s.Kda, "F2");
            Metric(MetricsPanel, "Participation kills", s.Participation, suffix: " %"); Metric(MetricsPanel, "CS / min", s.CsPerMinute);
            Metric(ExtraMetricsPanel, "Kills moyens", s.Kills); Metric(ExtraMetricsPanel, "Morts moyennes", s.Deaths); Metric(ExtraMetricsPanel, "Assists moyennes", s.Assists);
            Metric(ExtraMetricsPanel, "Dégâts / min", s.DamagePerMinute); Metric(ExtraMetricsPanel, "Or / min", s.GoldPerMinute); Metric(ExtraMetricsPanel, "Vision moyenne", s.Vision);
            Metric(ExtraMetricsPanel, "Balises posées", s.WardsPlaced); Metric(ExtraMetricsPanel, "Balises détruites", s.WardsKilled); Metric(ExtraMetricsPanel, "Balises contrôle", s.ControlWards);
        }
        var roles = Selected(RolesMode); RolesScope.Text = Coverage(roles);
        foreach (var role in roles?.Roles ?? [])
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(100) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new());
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(ProfileIcons.Create(role.Role, Roles.Labels.GetValueOrDefault(role.Role, "Inconnu"), 24));
            name.Children.Add(SeasonText(Roles.Labels.GetValueOrDefault(role.Role, "Inconnu"))); row.Children.Add(name);
            void Bar(int column, double percent, string label, Brush color)
            {
                var cell = new StackPanel { Margin = new Thickness(7, 0, 0, 0) };
                cell.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = percent, Height = 6, Foreground = color });
                cell.Children.Add(SeasonText(label, 11)); Grid.SetColumn(cell, column); row.Children.Add(cell);
            }
            Bar(1, 100.0 * role.Summary.Games / Math.Max(1, roles!.Roles.Sum(r => r.Summary.Games)), $"{role.Summary.Games} parties", Brushes.DeepSkyBlue);
            Bar(2, role.Summary.WinRate, $"{role.Summary.WinRate:F1} % V", Brushes.Turquoise); RolePanel.Children.Add(row);
        }
        var champions = Selected(ChampionsMode); ChampionsScope.Text = Coverage(champions);
        ChampionsGrid.ItemsSource = champions?.Champions.Select(c => new
        {
            ChampionVisual = Visual(c.ChampionId, c.Champion, true), Games = c.Summary.Games, WinRate = $"{c.Summary.WinRate:F0} %", Kda = $"{c.Summary.Kda:F2}",
            SeasonDetail = $"{c.Summary.Kills:F1} / {c.Summary.Deaths:F1} / {c.Summary.Assists:F1} · {c.Summary.CsPerMinute:F1} CS/min"
        }).ToArray();
    }
}
