using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rift.Core;
using Rift.Infrastructure;

namespace Rift.Desktop;

internal sealed class MatchDetailsWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBlock status = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 10, 20, 14) };
    private readonly MatchDetailsView view = new();
    private readonly Button retry = new() { Content = "Réessayer", Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Func<CancellationToken, Task<MatchDetails>> load;
    private readonly string assetsDirectory, puuid;
    private readonly bool demo;
    private readonly Func<string, CancellationToken, Task<PlayerRanks?>>? loadRanks;
    private readonly ProfileBitmapCache bitmaps;
    public Task Loading { get; private set; } = Task.CompletedTask;
    public MatchParticipant? RequestedPlayer { get; private set; }
    private bool debugEnabled, diagnosticStatus;
    public bool DebugEnabled
    {
        get => debugEnabled;
        set { debugEnabled = value; UpdateStatusVisibility(); }
    }
    internal TextBlock Status => status;
    internal void SetStatus(string message, bool diagnostic = false)
    {
        status.Text = message; diagnosticStatus = diagnostic; UpdateStatusVisibility();
    }
    private void UpdateStatusVisibility() => status.Visibility = diagnosticStatus && !debugEnabled ? Visibility.Collapsed : Visibility.Visible;
    public MatchDetailsWindow(Func<CancellationToken, Task<MatchDetails>> load, string assetsDirectory, string puuid, bool demo,
        Func<string, CancellationToken, Task<PlayerRanks?>>? loadRanks = null, ProfileBitmapCache? bitmaps = null)
    {
        this.load = load; this.assetsDirectory = assetsDirectory; this.puuid = puuid; this.demo = demo;
        this.loadRanks = loadRanks; this.bitmaps = bitmaps ?? new ProfileBitmapCache();
        view.PlayerRequested += player => { RequestedPlayer = player; Close(); };
        Title = "Détail de la partie · Rift Companion"; Width = Math.Min(1240, SystemParameters.WorkArea.Width); Height = Math.Min(940, SystemParameters.WorkArea.Height);
        MinWidth = 960; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var root = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 16, 20, 0) };
        var close = new Button { Content = "← Retour au profil", IsCancel = true }; close.Click += (_, _) => Close();
        var diagnostics = new Button { Content = "Diagnostic en direct", Margin = new Thickness(12, 0, 0, 0) }; diagnostics.Click += (_, _) => DiagnosticsWindow.Open(this);
        actions.Children.Add(diagnostics); actions.Children.Add(close); actions.Children.Add(retry); DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(view); Content = root;
        Loaded += (_, _) => Loading = Load();
        retry.Click += (_, _) => { if (Loading.IsCompleted) Loading = Load(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (view.RunesPopup.IsOpen) view.RunesPopup.IsOpen = false; else Close(); } };
        Closed += async (_, _) => { lifetime.Cancel(); try { await Loading; } finally { lifetime.Dispose(); } };
    }
    private async Task Load()
    {
        using var diagnosticOperation = RuntimeDiagnostics.Begin("Chargements", "Fiche de partie");
        retry.Visibility = Visibility.Collapsed; SetStatus("Chargement du détail de la partie…");
        var token = lifetime.Token;
        Task rankLoading = Task.CompletedTask;
        try
        {
            var details = await load(token); token.ThrowIfCancellationRequested();
            var presentation = new MatchDetailsPresentation(details, puuid, demo);
            view.DataContext = presentation;
            rankLoading = LoadRanks(presentation, token);
            SetStatus("Préparation des illustrations…", diagnostic: true);
            using var assets = new RiotAssets(assetsDirectory, bundledDirectory: Path.Combine(AppContext.BaseDirectory, "Assets", "Champions"));
            using var updateGate = new SemaphoreSlim(1);
            // Show the scoreboard immediately; decode frozen images away from the dispatcher.
            async Task UpdateImages()
            {
                await updateGate.WaitAsync(token);
                try
                {
                var updates = await Task.Run(() => presentation.Visuals.Select(v => (Visual: v,
                    Name: v.IsRune ? assets.RuneName(v.Id) : v.IsSpell ? assets.SpellName(v.Id) : v.IsChampion ? assets.ChampionName(v.Id, v.Code) : v.IsRoleQuest ? (v.Id == 0 ? v.Name : "Quête de rôle · " + assets.ItemName(v.Id)) : assets.ItemName(v.Id),
                    Price: v.IsChampion || v.IsRune || v.IsSpell ? null : assets.Item(v.Id)?.Price,
                    Components: (v.IsChampion || v.IsRune || v.IsSpell ? [] : assets.Item(v.Id)?.Components ?? []).Select(id => new ItemComponent(assets.ItemName(id), bitmaps.Get(assets.ItemImage(id), 64))).ToArray(),
                    Description: v.IsRune ? assets.RuneDescription(v.Id) : v.IsSpell ? assets.SpellDescription(v.Id) : v.IsChampion ? "" : assets.ItemDescription(v.Id),
                    Icon: bitmaps.Get(v.IsRune ? assets.RuneImage(v.Id) : v.IsSpell ? assets.SpellImage(v.Id) : v.IsChampion ? assets.ChampionImage(v.Id, v.Code) : assets.ItemImage(v.Id), v.IsChampion ? 120 : 64))).ToArray(), token);
                token.ThrowIfCancellationRequested();
                await Dispatcher.InvokeAsync(() => { foreach (var update in updates) { update.Visual.Update(update.Name, update.Icon, update.Description); update.Visual.UpdateRecipe(update.Price, update.Components); } }, System.Windows.Threading.DispatcherPriority.Background, token);
                }
                finally { updateGate.Release(); }
            }
            await UpdateImages(); // Bundled portraits are available before any catalogue/network work.
            await Task.Run(() => assets.PrepareAsync(presentation.AssetMatches, token, offlineOnly: true, spellIds: presentation.SpellIds), token);
            await UpdateImages();
            using var imageDeadline = CancellationTokenSource.CreateLinkedTokenSource(token); imageDeadline.CancelAfter(TimeSpan.FromSeconds(25));
            try { await Task.Run(() => assets.PrepareAsync(presentation.AssetMatches, imageDeadline.Token, changed: UpdateImages, spellIds: presentation.SpellIds), imageDeadline.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            await UpdateImages();
            SetStatus(assets.Notice, diagnostic: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { diagnosticOperation.Cancelled(); }
        catch (Exception ex) when (ex is RiotApiException or HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (token.IsCancellationRequested) return;
            SetStatus(ex is RiotApiException ? ex.Message : "Impossible de charger cette partie. Vérifie la connexion puis réessaie.");
            retry.Visibility = Visibility.Visible;
        }
        finally { await rankLoading; }
    }
    private async Task LoadRanks(MatchDetailsPresentation presentation, CancellationToken token)
    {
        using var diagnosticOperation = RuntimeDiagnostics.Begin("Chargements", "Rangs des participants");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        bool failed = false;
        foreach (var player in presentation.Teams.SelectMany(t => t.Players))
        {
            if (token.IsCancellationRequested) return;
            PlayerRanks? ranks = null;
            try
            {
                if (demo) ranks = new PlayerRanks([new(presentation.Queue == 440 ? "RANKED_FLEX_SR" : "RANKED_SOLO_5x5", "PLATINUM", "IV", 35, 10, 8)], DateTimeOffset.UtcNow);
                else if (!failed && loadRanks is not null) ranks = await loadRanks(player.Player.Puuid, deadline.Token);
                var entry = ranks?.Entries.FirstOrDefault(r => r.Queue == (presentation.Queue == 440 ? "RANKED_FLEX_SR" : "RANKED_SOLO_5x5"));
                var tier = entry?.Tier;
                var allowed = new[] { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER" };
                var icon = tier is not null && allowed.Contains(tier) ? await Task.Run(() => bitmaps.Get(Path.Combine(AppContext.BaseDirectory, "Assets", "Ranks", tier + ".png")), token) : null;
                if (!token.IsCancellationRequested) player.SetRank(ranks, presentation.Queue, icon, demo);
            }
            catch (Exception ex) when (ex is RiotApiException or HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException)
            {
                failed = true;
                if (!token.IsCancellationRequested) player.SetRank(null, presentation.Queue, null);
            }
        }
    }
}
