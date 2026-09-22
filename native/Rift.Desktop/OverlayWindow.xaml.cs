using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Rift.Core;
using Rift.Infrastructure;

namespace Rift.Desktop;

public partial class OverlayWindow : Window
{
    private readonly LiveRosterClient live = new();
    private readonly Func<CancellationToken, Task<IReadOnlyList<OverlayPlayer>>> readRoster;
    private readonly RiotAssets assets;
    private readonly ProfileBitmapCache images = new();
    private readonly Func<string, string, CancellationToken, Task<PlayerProfile>> loadProfile;
    private readonly Func<CancellationToken, Task<PlayerProfile?>> detect;
    private readonly Func<string, string, CancellationToken, Task<OpggProfile?>>? opggLoader;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly Dictionary<string, PlayerProfile> profileCache = [];
    private CancellationTokenSource? visibleLifetime, enrichmentLifetime;
    private Task monitor = Task.CompletedTask, enrichment = Task.CompletedTask, opggEnrichment = Task.CompletedTask;
    private bool stopping, example;
    private string signature = "";
    public event Action? DetachRequested;
    internal Task Monitoring => monitor;
    private bool dragging;
    private NativePoint dragStart;
    private NativeRect dragBounds;
    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (!GetCursorPos(out dragStart) || !GetWindowRect(new WindowInteropHelper(this).Handle, out dragBounds)) return;
        dragging = DragHandle.CaptureMouse(); e.Handled = true;
    }
    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) { DragHandle.ReleaseMouseCapture(); return; }
        if (GetCursorPos(out var cursor))
            SetWindowPos(new WindowInteropHelper(this).Handle, 0, dragBounds.Left + cursor.X - dragStart.X,
                dragBounds.Top + cursor.Y - dragStart.Y, 0, 0, 0x0010 | 0x0001 | 0x0004); // no activation, resize or z-order change
        e.Handled = true;
    }
    private void OnDragEnd(object sender, MouseButtonEventArgs e) { DragHandle.ReleaseMouseCapture(); e.Handled = true; }
    private void OnDragLost(object sender, MouseEventArgs e) => dragging = false;
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    public OverlayWindow(string dataDirectory, Func<string, string, CancellationToken, Task<PlayerProfile>> loadProfile, Func<CancellationToken, Task<PlayerProfile?>> detect, Func<CancellationToken, Task<IReadOnlyList<OverlayPlayer>>>? rosterReader = null, Func<string, string, CancellationToken, Task<OpggProfile?>>? opggLoader = null)
    {
        InitializeComponent(); readRoster = rosterReader ?? live.ReadAsync; this.loadProfile = loadProfile; this.detect = detect; this.opggLoader = opggLoader;
        assets = new(Path.Combine(dataDirectory, "riot-assets"), bundledDirectory: Path.Combine(AppContext.BaseDirectory, "Assets", "Champions"));
        IsVisibleChanged += async (_, _) => await UpdateVisibility();
        StateChanged += async (_, _) => await UpdateVisibility();
        Closing += (_, e) => { if (!stopping) { e.Cancel = true; Hide(); } };
    }
    private void OnHide(object sender, RoutedEventArgs e) => Hide();
    private void OnDetach(object sender, RoutedEventArgs e) => DetachRequested?.Invoke();
    private async Task UpdateVisibility()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (stopping) return;
            if (IsVisible && WindowState != WindowState.Minimized && !stopping && !example)
            {
                if (!monitor.IsCompleted && visibleLifetime?.IsCancellationRequested == false) return;
                await monitor; visibleLifetime?.Dispose(); visibleLifetime = new();
                monitor = Monitor(visibleLifetime.Token);
            }
            else { visibleLifetime?.Cancel(); await monitor; }
        }
        finally { lifecycle.Release(); }
    }
    private async Task Monitor(CancellationToken token)
    {
        bool firstRead = true;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var roster = await Task.Run(() => readRoster(token), token);
                    if (roster.Count > 10) { Status.Text = "Ce mode comporte plus de dix joueurs ; vue 5 contre 5 indisponible."; roster = []; }
                    var next = System.Text.Json.JsonSerializer.Serialize(roster.Select(p => new { p.RiotId, p.Team, p.Champion, p.Keystone }));
                    if (next != signature || firstRead)
                    {
                        enrichmentLifetime?.Cancel(); await Task.WhenAll(enrichment, opggEnrichment); enrichmentLifetime?.Dispose();
                        var rows = next == signature && Players.ItemsSource is OverlayCard[] existing ? existing :
                            await Task.Run(() => roster.Select(p => new OverlayCard(p, assets, images)).ToArray(), token);
                        token.ThrowIfCancellationRequested();
                        if (!ReferenceEquals(Players.ItemsSource, rows)) Players.ItemsSource = rows;
                        signature = next; firstRead = false;
                        enrichmentLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                        enrichment = Enrich(rows, enrichmentLifetime.Token);
                        opggEnrichment = EnrichOpgg(rows, enrichmentLifetime.Token);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException || ex is OperationCanceledException && !token.IsCancellationRequested)
                {
                    enrichmentLifetime?.Cancel(); await enrichment;
                    firstRead = true;
                    Status.Text = "Connexion locale indisponible · dernières cartes conservées, non actualisées · nouvel essai dans 15 s.";
                }
                await Task.Delay(TimeSpan.FromSeconds(15), token);
            }
        }
        catch (OperationCanceledException) { }
        finally { enrichmentLifetime?.Cancel(); await Task.WhenAll(enrichment, opggEnrichment); }
    }
    private async Task EnrichOpgg(OverlayCard[] rows, CancellationToken token)
    {
        if (opggLoader is null || rows.Length == 0) return;
        try
        {
            var owner = await detect(token); if (owner is null) return;
            foreach (var row in rows.Where(r => r.Player.CanLookup))
            {
                var value = await opggLoader(row.Player.RiotId, owner.Platform, token);
                token.ThrowIfCancellationRequested(); row.ApplyOpgg(value);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException)
        { foreach (var row in rows.Where(r => r.Player.CanLookup)) row.ApplyOpgg(null); }
    }
    private async Task Enrich(OverlayCard[] rows, CancellationToken token)
    {
        using var operation = RuntimeDiagnostics.Begin("Overlay", "Profils des participants");
        try
        {
            if (rows.Length == 0) { Status.Text = "Aucun joueur révélé dans la partie."; return; }
            var owner = await detect(token);
            if (owner is null) { Status.Text = "Joueurs affichés. Serveur LoL non détecté : statistiques historiques non chargées."; return; }
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; token.ThrowIfCancellationRequested();
                Status.Text = $"{owner.Platform.ToUpperInvariant()} · historique {i + 1}/{rows.Length} · 10 parties/joueur · priorité au profil · quotas partagés";
                if (!row.Player.CanLookup) { row.Notice = row.Player.IsBot ? "Bot d’entraînement · pas de profil classé." : "Identité non révélée : aucune recherche."; row.Changed(); continue; }
                try
                {
                    var cacheKey = owner.Platform + ":" + row.Player.RiotId;
                    if (!profileCache.TryGetValue(cacheKey, out var profile) || DateTimeOffset.UtcNow - profile.LoadedAt > TimeSpan.FromMinutes(10))
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(3));
                        profile = await loadProfile(row.Player.RiotId, owner.Platform, deadline.Token);
                        if (profileCache.Count >= 20) profileCache.Clear(); profileCache[cacheKey] = profile;
                    }
                    token.ThrowIfCancellationRequested();
                    await Task.Run(() => assets.PrepareAsync([], token, profileIconId: profile.ProfileIconId), token);
                    var visual = await Task.Run(() => OverlayCard.ProfileImages(profile, assets, images), token);
                    token.ThrowIfCancellationRequested(); row.Apply(profile, visual.Icon, visual.Rank);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is RiotApiException or ArgumentException or IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
                {
                    row.Notice = ex is RiotApiException ? ex.Message : "Historique indisponible pour ce joueur."; row.Changed();
                    // A rejected key or quota must not trigger ten identical failures.
                    if (ex is RiotApiException { PlayerUnavailable: false }) { Status.Text = row.Notice; return; }
                }
            }
            Status.Text = $"{owner.Platform.ToUpperInvariant()} · données actualisées à {DateTime.Now:HH:mm} · identités vérifiées toutes les 15 s · profils en cache 10 min";
        }
        catch (OperationCanceledException) { operation.Cancelled(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException or ArgumentException)
        { operation.Failed(); Status.Text = "Connexion locale indisponible pour identifier le serveur."; }
    }
    public async Task ShowExample()
    {
        example = true; visibleLifetime?.Cancel(); await monitor; signature = "";
        var profile = ProfileDemo.Create(); profile = profile with { Matches = profile.Matches.Take(10).ToArray() }; var details = ProfileDemo.Details(profile.Matches[1]);
        var rows = await Task.Run(() => details.Participants.Select(p => new OverlayCard(new(p.RiotId, p.TeamId == 100 ? "ORDER" : "CHAOS", p.Stats.Champion, p.Level, p.Stats.Role, p.Runes.Keystone, ["SummonerFlash", "SummonerDot"]), assets, images)).ToArray());
        var demoImages = await Task.Run(() => OverlayCard.ProfileImages(profile, assets, images));
        foreach (var row in rows) row.Apply(profile with { RiotId = row.Name }, demoImages.Icon, demoImages.Rank);
        if (stopping) return;
        Players.ItemsSource = rows; Status.Text = "DÉMONSTRATION FICTIVE · aucune requête réseau · Ctrl+X : masquer · Ctrl+Maj+X : changer d’écran";
        Show();
    }
    public void UseLive() { example = false; }
    public async Task StopAsync()
    {
        stopping = true; Hide(); visibleLifetime?.Cancel(); await lifecycle.WaitAsync();
        try { await monitor; live.Dispose(); assets.Dispose(); visibleLifetime?.Dispose(); enrichmentLifetime?.Dispose(); Close(); }
        finally { lifecycle.Release(); }
    }
}

internal sealed class OverlayCard : INotifyPropertyChanged
{
    private OpggProfile? opgg;
    private int championId;
    public string ServerRank { get; private set; } = "";
    public string OpggDetails { get; private set; } = "";
    public void ApplyOpgg(OpggProfile? value)
    {
        opgg = value;
        if (value is null) { ServerRank = "Données indisponibles"; Changed(); return; }
        ServerRank = value.Rank is > 0 && value.Total >= value.Rank ? $"Serveur · #{value.Rank:N0}\nTop {100.0 * value.Rank / value.Total:F2} %" : "Rang serveur indisponible";
        OpggDetails = value.RankLabel + "\n" + value.Scope + "\n" + value.DateLabel +
            (DateTimeOffset.UtcNow - value.FetchedAt >= TimeSpan.FromHours(1) ? " · cache ancien" : " · cache 1 h") + "\nClassement mondial/Flex distinct indisponible. Statistiques Riot récentes conservées si champion absent de la liste.";
        if (value.Champions.FirstOrDefault(c => c.Id == championId) is { } c)
        {
            Kills = c.AverageKills.ToString("F1"); Deaths = c.AverageDeaths.ToString("F1"); Assists = c.AverageAssists.ToString("F1");
            ChampionStats = $"{c.WinRate:F1} % · {c.Games} parties\nS{value.Season?.ToString() ?? "?"} · {value.Queue switch { "RANKED" => "Classé", "SOLORANKED" => "Solo", "FLEXRANKED" => "Flex", _ => value.Queue }}";
        }
        Changed();
    }
    public OverlayPlayer Player { get; }
    public string Name => Player.CanLookup || Player.IsBot ? Player.RiotId : "Identité non révélée";
    public string Accent => Player.Team == "ORDER" ? "#47BEE5" : "#F08394";
    public ProfileVisual Champion { get; }
    public ProfileVisual Rune { get; }
    public ProfileVisual[] Spells { get; }
    public ImageSource? ProfileIcon { get; private set; }
    public ImageSource? RankIcon { get; private set; }
    public ImageSource? RoleIcon { get; private set; }
    public string AccountLevel { get; private set; } = "Niveau du compte : —";
    public string Rank { get; private set; } = "Rang : —";
    public string RankedWinRate { get; private set; } = "";
    public string Kills { get; private set; } = "—";
    public string Deaths { get; private set; } = "—";
    public string Assists { get; private set; } = "—";
    public string ChampionStats { get; private set; } = "Historique en attente…";
    public string Recent12Hours { get; private set; } = "—";
    public string Recent30Days { get; private set; } = "—";
    public string MainRole { get; private set; } = "—";
    public string[] Tags { get; private set; } = [];
    public string Notice { get; set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Changed() => PropertyChanged?.Invoke(this, new(""));
    public OverlayCard(OverlayPlayer player, RiotAssets assets, ProfileBitmapCache images)
    {
        Player = player;
        championId = assets.Champion(0, player.Champion)?.Id ?? 0;
        if (player.IsBot) { AccountLevel = "Bot d’entraînement"; Rank = "Sans classement"; ChampionStats = "Pas d’historique joueur"; Notice = "Aucune recherche Riot pour ce bot."; }
        Champion = new(0, player.Champion, true); Champion.Update(assets.ChampionName(0, player.Champion), images.Get(assets.ChampionImage(0, player.Champion), 120));
        Rune = new(player.Keystone, "", false) { IsRune = true }; Rune.Update(assets.RuneName(player.Keystone), images.Get(assets.RuneImage(player.Keystone), 64), assets.RuneDescription(player.Keystone));
        Spells = player.Spells.Select(code => { var id = assets.SpellId(code); var v = new ProfileVisual(id, "", false) { IsSpell = true }; v.Update(assets.SpellName(id), images.Get(assets.SpellImage(id), 64), assets.SpellDescription(id)); return v; }).ToArray();
    }
    public static (ImageSource? Icon, ImageSource? Rank) ProfileImages(PlayerProfile p, RiotAssets assets, ProfileBitmapCache images)
    {
        var tier = p.Ranks.FirstOrDefault(r => r.Queue == "RANKED_SOLO_5x5")?.Tier;
        var allowed = new[] { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER" };
        return (images.Get(assets.ProfileIconImage(p.ProfileIconId), 96), allowed.Contains(tier) ? images.Get(Path.Combine(AppContext.BaseDirectory, "Assets", "Ranks", tier + ".png"), 96) : null);
    }
    public void Apply(PlayerProfile profile, ImageSource? icon, ImageSource? rankIcon)
    {
        var stats = OverlayStats.From(profile, Player.Champion, DateTimeOffset.UtcNow);
        ProfileIcon = icon; RankIcon = rankIcon; AccountLevel = $"Niveau {profile.Level}";
        var rank = profile.Ranks.FirstOrDefault(r => r.Queue == "RANKED_SOLO_5x5");
        Rank = rank is null ? "Solo/Duo : non classé" : $"{rank.Tier} {rank.Division}\n{rank.Lp} LP";
        RankedWinRate = rank is null || rank.Wins + rank.Losses == 0 ? "" : $"{100.0 * rank.Wins / (rank.Wins + rank.Losses):F0} % ({rank.Wins}/{rank.Wins + rank.Losses})";
        Kills = stats.Kills; Deaths = stats.Deaths; Assists = stats.Assists; ChampionStats = stats.ChampionStats; Recent12Hours = stats.Recent12Hours; Recent30Days = stats.Recent30Days; MainRole = stats.MainRole; Tags = stats.Tags;
        var role = Roles.Labels.FirstOrDefault(p => p.Value == MainRole).Key;
        RoleIcon = role is null ? null : ProfileIcons.Create(role, MainRole).Source;
        Notice = $"Échantillon Riot : {profile.Matches.Count} parties · {profile.LoadedAt.ToLocalTime():HH:mm}";
        if (opgg is not null) ApplyOpgg(opgg); else Changed();
    }
}
