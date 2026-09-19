using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Rift.Core;
using Rift.Infrastructure;

namespace Rift.Desktop;

public partial class MainWindow : Window
{
    private readonly SettingsStore store;
    private readonly LcuMonitor monitor;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? polling;
    private Task pollTask = Task.CompletedTask;
    private readonly SemaphoreSlim modeGate = new(1, 1);
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly DispatcherTimer metricsTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Process process = Process.GetCurrentProcess();
    private TimeSpan lastCpu;
    private long lastSample = Stopwatch.GetTimestamp();
    private string? directory;
    private bool initialized, storageAvailable, demo, closing, canClose;
    private readonly string dbPath;
    private string? previousDraft;

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        demo = args.Contains("--demo");
        var dataIndex = Array.IndexOf(args, "--data-dir");
        var dataDirectory = dataIndex >= 0 && dataIndex + 1 < args.Length ? Path.GetFullPath(args[dataIndex + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RiftCompanion");
        dbPath = Path.Combine(dataDirectory, "settings.db");
        store = new SettingsStore(dbPath);
        ProfilePage.Configure(dataDirectory);
        monitor = new LcuMonitor(new LcuTransport(), token => LockfileDiscovery.FindAsync(directory, token));
        RoleBox.ItemsSource = Roles.Labels;
        lastCpu = process.TotalProcessorTime;
        metricsTimer.Tick += (_, _) => SampleMetrics();
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await Task.Run(() => { store.Initialize(); return (store.PreferredRole, store.Get("lolDirectory")); });
            RoleBox.SelectedValue = settings.PreferredRole; directory = settings.Item2; storageAvailable = true;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { RoleBox.SelectedValue = "jungle"; StorageText.Text = "Stockage indisponible : les préférences ne seront pas conservées."; }
        if (closing) return;
        initialized = true;
        DatabaseText.Text = $"Base locale : {dbPath}";
        UpdateDirectory();
        ProfilePage.StartPersonalProfiles(token => LockfileDiscovery.FindAsync(directory, token), !demo);
        // The profile works independently of the local League client.
        if (draftPageActive) await RestartMode();
        metricsTimer.Start(); SampleMetrics();
    }
    private async Task RestartMode()
    {
        await modeGate.WaitAsync();
        try
        {
            polling?.Cancel();
            try { await pollTask; } catch (OperationCanceledException) { }
            polling?.Dispose();
            if (closing || !draftPageActive) return;
            previousDraft = null;
            ModeButton.Content = demo ? "Revenir à LoL" : "Voir une démonstration";
            if (demo) Render(DemoState(), DemoNames);
            else
            {
                Render(ClientState.Offline("Connexion en cours…"), monitor.Champions);
                polling = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var token = polling.Token;
                pollTask = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        var state = await monitor.TickAsync(token);
                        await Dispatcher.InvokeAsync(() => { if (!closing && !demo) Render(state, monitor.Champions); });
                        await Task.Delay(monitor.IntervalMs, token);
                    }
                }, token);
            }
        }
        finally { modeGate.Release(); }
    }
    private bool draftPageActive;
    private async void OnPageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        draftPageActive = ((TabControl)sender).SelectedIndex == 1;
        if (initialized) await RestartMode();
    }
    private async void OnModeClicked(object sender, RoutedEventArgs e)
    {
        ModeButton.IsEnabled = false;
        try { demo = !demo; await RestartMode(); } finally { ModeButton.IsEnabled = true; }
    }
    private async Task Save(string key, string value)
    {
        if (!storageAvailable || closing) return;
        await saveGate.WaitAsync();
        try { await Task.Run(() => store.Set(key, value)); StorageText.Text = ""; }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        { StorageText.Text = "La préférence n’a pas pu être enregistrée."; }
        finally { saveGate.Release(); }
    }
    private async void OnRoleChanged(object sender, SelectionChangedEventArgs e)
    { if (initialized && RoleBox.SelectedValue is string role) await Save("preferredRole", Roles.Validate(role)); }
    private async void OnDirectoryClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Dossier contenant LeagueClient.exe" };
        if (dialog.ShowDialog(this) != true) return;
        directory = dialog.FolderName; UpdateDirectory(); await Save("lolDirectory", directory); await RestartMode();
    }
    private void UpdateDirectory() => DirectoryText.Text = string.IsNullOrEmpty(directory) ? "Installation : détection automatique (C: / D:)" : $"Installation : {directory}";
    private void Render(ClientState state, IReadOnlyDictionary<int, string> names)
    {
        StatusText.Text = demo ? "Démonstration · données fictives" : $"{(state.Connected ? "Client connecté" : "En attente de LoL")} · {PhaseLabel(state.Phase)}";
        MessageText.Text = demo ? "Aucune requête vers LoL pendant la démonstration." : state.Message;
        string Name(int id) => names.TryGetValue(id, out var name) && name.Length > 0 ? name : $"Champion #{id}";
        var signature = System.Text.Json.JsonSerializer.Serialize(new { state.Draft, Names = names });
        if (signature == previousDraft) return;
        previousDraft = signature;
        void Team(ItemsControl list, IReadOnlyList<Player>? players)
        {
            list.Items.Clear();
            if (players is { Count: 0 }) { list.Items.Add(new TextBlock { Text = "Aucun joueur révélé", Margin = new Thickness(0, 14, 0, 14) }); return; }
            foreach (var player in players ?? Enumerable.Repeat(new Player(0, 0, "", false), 5).ToArray())
            {
                var title = player.ChampionId > 0 ? Name(player.ChampionId) : player.IntentId > 0 ? Name(player.IntentId) + " · pré-sélection" : "En attente";
                var role = Roles.Labels.GetValueOrDefault(player.Role, "Rôle non révélé");
                var panel = new StackPanel();
                panel.Children.Add(new TextBlock { Text = title + (player.IsYou ? "  · TOI" : ""), FontWeight = FontWeights.SemiBold });
                panel.Children.Add(new TextBlock { Text = role, FontSize = 11, Foreground = Brushes.LightSlateGray, Margin = new Thickness(0, 4, 0, 0) });
                list.Items.Add(new Border { Child = panel, Padding = new Thickness(10), Margin = new Thickness(0, 3, 0, 3), CornerRadius = new CornerRadius(5), Background = player.IsYou ? new SolidColorBrush(Color.FromRgb(34, 57, 49)) : Brushes.Transparent });
            }
        }
        Team(AlliesList, state.Draft?.Allies); Team(EnemiesList, state.Draft?.Enemies);
        AllyBans.Text = "Bans : " + (state.Draft?.AllyBans.Count > 0 ? string.Join(" · ", state.Draft.AllyBans.Select(Name)) : "—");
        EnemyBans.Text = "Bans : " + (state.Draft?.EnemyBans.Count > 0 ? string.Join(" · ", state.Draft.EnemyBans.Select(Name)) : "—");
    }
    private void SampleMetrics()
    {
        process.Refresh(); var now = Stopwatch.GetTimestamp(); var cpu = process.TotalProcessorTime;
        var percent = (cpu - lastCpu).TotalSeconds / Math.Max(.001, Stopwatch.GetElapsedTime(lastSample, now).TotalSeconds) / Environment.ProcessorCount * 100;
        lastCpu = cpu; lastSample = now;
        MetricsText.Text = $"CPU {percent:F2} %   ·   RAM {process.WorkingSet64 / 1048576.0:F1} Mo   ·   {monitor.Requests} requêtes LoL   ·   {monitor.Errors} cycles en erreur";
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (canClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; Hide(); metricsTimer.Stop();
        // Hide immediately; cancellation callbacks and HTTP disposal can take time.
        await Task.WhenAll(lifetime.CancelAsync(), ProfilePage.StopAsync());
        await modeGate.WaitAsync();
        try { try { await pollTask; } catch (OperationCanceledException) { } }
        finally { modeGate.Release(); }
        await saveGate.WaitAsync(); saveGate.Release();
        await Task.Run(monitor.Dispose); polling?.Dispose(); lifetime.Dispose(); process.Dispose();
        canClose = true; Close();
    }
    private static string PhaseLabel(string phase) => phase switch
    { "Offline" => "Client fermé ou inaccessible", "None" => "Accueil", "Lobby" => "Salon", "Matchmaking" => "Recherche de partie", "ReadyCheck" => "Partie trouvée", "ChampSelect" => "Sélection des champions", "InProgress" => "En partie", "Reconnect" => "Reconnexion", "GameStart" => "Chargement", "WaitingForStats" or "PreEndOfGame" or "EndOfGame" => "Fin de partie", _ => phase };
    private static readonly IReadOnlyDictionary<int, string> DemoNames = new Dictionary<int, string> { [516] = "Ornn", [64] = "Lee Sin", [103] = "Ahri", [222] = "Jinx", [412] = "Thresh", [58] = "Renekton", [234] = "Viego", [112] = "Viktor", [145] = "Kai’Sa", [89] = "Leona", [35] = "Shaco", [238] = "Zed" };
    private static ClientState DemoState()
    {
        string[] roles = ["top", "jungle", "middle", "bottom", "utility"];
        Player[] Team(int[] ids, bool ally) => ids.Select((id, i) => new Player(id, 0, roles[i], ally && i == 1)).ToArray();
        return new(false, "ChampSelect", new Draft(Team([516, 64, 103, 222, 412], true), Team([58, 234, 112, 145, 89], false), [35], [238]), "");
    }
}
