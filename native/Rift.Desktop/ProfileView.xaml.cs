using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rift.Core;
using Rift.Infrastructure;

namespace Rift.Desktop;

public partial class ProfileView : UserControl
{
    private readonly RiotProfileClient api = new();
    private ProfileCache? cache;
    private PlayerProfile? profile;
    private CancellationTokenSource? loadCancellation;
    private Task loading = Task.CompletedTask;
    private RiotAssets? assets;
    private QueueCatalogUpdater? queueCatalog;
    private readonly ProfileBitmapCache bitmaps = new();
    private readonly Dictionary<string, ProfileVisual> visuals = [];
    private readonly ObservableCollection<ProfileMatchRow> matchRows = [];
    private bool isPreparing;
    private bool preparingImages;
    private bool appending;
    private double? appendScrollOffset;
    private bool stopping, preferencesLoaded;
    private ApiKeyVault? vault;
    private ProfileHistory? history;
    private IReadOnlyList<RecentProfile> recent = [];
    private Task preferencesTask = Task.CompletedTask;
    private Task startup = Task.CompletedTask;
    private PersonalProfileStore? personalStore;
    private PersonalProfileDetector? personalDetector;
    private PlayerProfile? personalProfile;
    private readonly CancellationTokenSource personalLifetime = new();
    private Task personalPolling = Task.CompletedTask;
    private bool browsingOther;
    private PlayerProfile? displayedPersonal;
    private BitmapSource? preparedProfileIcon;
    private readonly SemaphoreSlim personalSaveGate = new(1, 1);
    private readonly Dictionary<string, BitmapSource?> ranks = [];
    public ProfileView()
    {
        InitializeComponent();
        MatchesGrid.ItemsSource = matchRows;

        ServerBox.ItemsSource = RiotProfileClient.Platforms.ToDictionary(x => x.Key, x => x.Value.Label); ServerBox.SelectedValue = "euw1";
        QueueBox.ItemsSource = new Dictionary<int, string> { [0] = "Toutes les files", [420] = "Solo/Duo", [440] = "Flex", [450] = "ARAM" }; QueueBox.SelectedValue = 0;
    }
    public void Configure(string directory)
    {
        cache = new ProfileCache(Path.Combine(directory, "profile-cache.db"));
        assets = new RiotAssets(Path.Combine(directory, "riot-assets"));
        queueCatalog = new QueueCatalogUpdater(directory);
        vault = new ApiKeyVault(directory); history = new ProfileHistory(directory);
        personalStore = new PersonalProfileStore(directory);
    }
    public void StartPersonalProfiles(Func<CancellationToken, Task<Credentials?>> discover, bool detect)
    {
        if (preferencesLoaded || vault is null || history is null) return;
        preferencesLoaded = true;
        startup = RestorePreferences();
        if (detect) personalDetector = new PersonalProfileDetector(new LcuTransport(), discover);
        personalPolling = RunPersonalProfiles(personalLifetime.Token);
    }
    private async Task RestorePreferences()
    {
        try
        {
            var stored = await Task.Run(() => (Key: vault!.Read(), Recent: history!.Read()));
            if (stopping) return;
            if (ApiKeyBox.Password.Length == 0 && stored.Key is not null) { ApiKeyBox.Password = stored.Key; KeyStatus.Text = "Clé restaurée depuis le stockage chiffré Windows."; }
            recent = stored.Recent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { KeyStatus.Text = "Clé enregistrée inaccessible. Tu peux saisir une nouvelle clé."; }
        try { personalProfile = await Task.Run(() => personalStore!.Read()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { PersonalStatus.Text = "Le profil personnel enregistré est inaccessible."; }
    }

    private async Task SavePersonal(PlayerProfile value)
    {
        personalProfile = value;
        await personalSaveGate.WaitAsync();
        try { await Task.Run(() => personalStore!.Save(value)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { PersonalStatus.Text = "Profil personnel disponible, mais sa sauvegarde a échoué."; }
        finally { personalSaveGate.Release(); }
    }

    private async Task RunPersonalProfiles(CancellationToken token)
    {
        try
        {
            await startup;
            while (!token.IsCancellationRequested)
            {
                // Do not compete with a profile request or a manual navigation.
                if (!isPreparing && loading.IsCompleted && preferencesTask.IsCompleted)
                {
                    var detected = personalDetector is null ? null : await Task.Run(() => personalDetector.DetectAsync(token), token);
                    if (detected is not null && !PersonalProfileStore.SameAccount(detected, personalProfile))
                        await SavePersonal(detected);
                    else if (detected?.ProfileIconId is not null && personalProfile is not null && detected.ProfileIconId != personalProfile.ProfileIconId)
                        await SavePersonal(personalProfile with { ProfileIconId = detected.ProfileIconId });
                    if (stopping) return;
                    if (personalProfile is null && profile is null && !browsingOther && NoticePanel.Visibility != Visibility.Visible)
                        ShowNotice("Ouvre LoL pour retrouver ton profil, ou recherche un joueur.");
                    PersonalStatus.Text = personalProfile is null
                        ? "Ouvre le client LoL pour détecter ton compte. Tu peux aussi rechercher un joueur."
                        : $"Mon profil : {personalProfile.RiotId} · {(detected is null ? "dernier compte mémorisé" : "compte détecté dans LoL")}";
                    // A search started during discovery always wins navigation.
                    if (!browsingOther && !isPreparing && loading.IsCompleted && preferencesTask.IsCompleted && personalProfile is not null &&
                        (!PersonalProfileStore.SameAccount(displayedPersonal, personalProfile) || displayedPersonal?.ProfileIconId != personalProfile.ProfileIconId))
                        await OpenPersonal(false);
                }
                await Task.Delay(TimeSpan.FromSeconds(15), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || stopping) { }
    }

    private async void OnPersonalProfile(object sender, RoutedEventArgs e)
    {
        if (isPreparing || !loading.IsCompleted || !startup.IsCompleted || !preferencesTask.IsCompleted || stopping) return;
        browsingOther = false;
        if (personalProfile is null) { ShowNotice("Ouvre le client LoL pour détecter ton compte, ou recherche un joueur."); return; }
        await OpenPersonal(ReferenceEquals(sender, RefreshPersonalButton));
    }

    private async Task OpenPersonal(bool refresh)
    {
        var saved = personalProfile!;
        displayedPersonal = saved;
        RiotIdBox.Text = saved.RiotId; ServerBox.SelectedValue = saved.Platform;
        BeginLoading();
        loading = ShowPersonal(saved, refresh, loadCancellation!.Token);
        try { await loading; } finally { EndLoading(); }
    }

    private async Task ShowPersonal(PlayerProfile saved, bool refresh, CancellationToken token)
    {
        try
        {
            profile = saved;
            await LoadAssets(token, true);
            RevealProfile(token);
            ProfileStatus.Text = saved.RequestedMatches == 0 ? saved.Notice : $"Profil enregistré le {saved.LoadedAt.ToLocalTime():dd/MM/yyyy à HH:mm}.";
            if (ApiKeyBox.Password.Length == 0)
            {
                ProfileStatus.Text += " Ajoute une clé Riot pour actualiser les statistiques.";
                ShowNotice("Ajoute une clé Riot dans les paramètres de connexion pour actualiser ton profil.");
                if (saved.ProfileIconId is not null && preparedProfileIcon is null)
                {
                    await LoadAssets(token);
                    PlayerIcon.Source = preparedProfileIcon;
                    PlayerIconFallback.Visibility = preparedProfileIcon is null ? Visibility.Visible : Visibility.Collapsed;
                }
                return;
            }
            if (refresh || saved.RequestedMatches == 0 || saved.LoadedAt < DateTimeOffset.UtcNow.AddMinutes(-5))
                await LoadProfile(token, preserveCurrent: true);
        }
        catch (OperationCanceledException) { ProfileStatus.Text = "Chargement annulé."; ShowNotice(ProfileStatus.Text); }
    }
    private async Task PersistKey(string key)
    {
        try { if (vault is not null) { await Task.Run(() => vault.Save(key)); if (!stopping) KeyStatus.Text = "Clé enregistrée et chiffrée pour ton compte Windows."; } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.Cryptography.CryptographicException)
        { if (!stopping) KeyStatus.Text = "Impossible d’enregistrer la clé. Elle reste utilisable pour cette session."; }
    }
    private async void OnSaveKey(object sender, RoutedEventArgs e)
    {
        if (!preferencesTask.IsCompleted || isPreparing || stopping || !startup.IsCompleted) return;
        preferencesTask = PersistKey(ApiKeyBox.Password); await preferencesTask;
    }
    private async void OnForgetKey(object sender, RoutedEventArgs e)
    {
        if (!preferencesTask.IsCompleted || isPreparing || stopping || !startup.IsCompleted || vault is null) return;
        preferencesTask = ForgetKey(); await preferencesTask;
    }
    private async Task ForgetKey()
    {
        try { await Task.Run(vault!.Forget); ApiKeyBox.Clear(); KeyStatus.Text = "Clé enregistrée supprimée."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { KeyStatus.Text = "Impossible de supprimer la clé enregistrée."; }
    }
    private void OnRiotIdChanged(object sender, TextChangedEventArgs e)
    {
        if (Suggestions is null || isPreparing) return;
        var rows = ProfileHistory.Suggest(recent, RiotIdBox.Text);
        Suggestions.ItemsSource = rows;
        SuggestionsPopup.IsOpen = rows.Count > 0 && RiotIdBox.IsKeyboardFocusWithin;
    }
    private void OnRiotIdFocus(object sender, RoutedEventArgs e) => OnRiotIdChanged(sender, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
    private void OnSuggestionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Suggestions.SelectedItem is not RecentProfile selected) return;
        RiotIdBox.Text = selected.RiotId; ServerBox.SelectedValue = selected.Platform;
        SuggestionsPopup.IsOpen = false; RiotIdBox.CaretIndex = RiotIdBox.Text.Length;
    }
    private async void OnLoadProfile(object sender, RoutedEventArgs e)
    {
        if (cache is null || !loading.IsCompleted || !preferencesTask.IsCompleted || !startup.IsCompleted || stopping) return;
        browsingOther = true;
        BeginLoading();
        loading = LoadProfile(loadCancellation!.Token);
        try { await loading; } finally { EndLoading(); }
    }
    private async Task LoadProfile(CancellationToken token, PlayerProfile? previous = null, bool preserveCurrent = false)
    {
        bool completed = false;
        var fallback = previous ?? (preserveCurrent ? profile : null);
        try
        {
            var id = previous?.RiotId ?? RiotIdBox.Text; var server = previous?.Platform ?? ServerBox.SelectedValue as string ?? "euw1"; var key = ApiKeyBox.Password;
            var matchCount = previous is null ? 20 : 10;
            var progress = new Progress<string>(message => { if (isPreparing && !preparingImages) LoadingStage.Text = message; });
            if (preserveCurrent) { LoadingPanel.Visibility = Visibility.Collapsed; LoadingBar.IsIndeterminate = false; ProfileStatus.Text = "Actualisation du profil…"; }
            profile = await Task.Run(() => api.LoadAsync(id, server, key, cache!, progress, token, matchCount: matchCount, previous: previous), token);
            if (previous is null) await PersistKey(key);
            try { if (history is not null) recent = await Task.Run(() => history.Remember(profile.RiotId, profile.Platform), token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { KeyStatus.Text = "Historique des recherches non enregistré."; }
            await LoadAssets(token);
            RevealProfile(token); completed = true;
            if (PersonalProfileStore.SameAccount(profile, personalProfile)) await SavePersonal(profile!);
        }
        catch (OperationCanceledException) { ProfileStatus.Text = token.IsCancellationRequested ? "Chargement annulé." : "Riot met trop de temps à répondre. Réessaie plus tard."; }
        catch (Exception ex) when (ex is RiotApiException or ArgumentException) { ProfileStatus.Text = ex.Message; }
        catch (System.Net.Http.HttpRequestException) { ProfileStatus.Text = "Connexion à Riot impossible. Vérifie ta connexion réseau."; }
        catch (System.Text.Json.JsonException) { ProfileStatus.Text = "Format de données Riot inattendu. Le profil n’a pas été affiché."; }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        { ProfileStatus.Text = "Le cache local est indisponible. Vérifie l’accès au dossier de données."; }
        finally { if (!completed && fallback is not null) profile = fallback; if (!completed && !stopping) ShowNotice(ProfileStatus.Text); }
    }
    private async void OnLoadMore(object sender, RoutedEventArgs e)
    {
        if (profile is not { HasMore: true, Demo: false } previous || isPreparing || !loading.IsCompleted || !preferencesTask.IsCompleted || stopping) return;
        appending = true; appendScrollOffset = null;
        BeginLoading(true);
        loading = LoadProfile(loadCancellation!.Token, previous);
        try { await loading; }
        finally
        {
            var offset = appendScrollOffset ?? ProfileScroll.VerticalOffset;
            EndLoading();
            await Dispatcher.InvokeAsync(() =>
            {
                ProfileScroll.UpdateLayout();
                ProfileScroll.ScrollToVerticalOffset(offset);
                appending = false;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
    private void OnProfileBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // Rebuilding filter/stat controls must not scroll away from the history.
        if (appending) e.Handled = true;
    }
    private void OnCancel(object sender, RoutedEventArgs e) => loadCancellation?.Cancel();
    private void BeginLoading(bool append = false)
    {
        loadCancellation?.Dispose(); loadCancellation = new CancellationTokenSource();
        isPreparing = true; preparingImages = false;
        PersonalButton.IsEnabled = RefreshPersonalButton.IsEnabled = false;
        SuggestionsPopup.IsOpen = false;
        LoadButton.IsEnabled = DemoButton.IsEnabled = RiotIdBox.IsEnabled = ServerBox.IsEnabled = false;
        CancelButton.IsEnabled = true; SaveKeyButton.IsEnabled = ForgetKeyButton.IsEnabled = false;
        MoreButton.Content = "Chargement…"; QueueBox.IsEnabled = false;
        if (!append) { profile = null; visuals.Clear(); matchRows.Clear(); }
        if (!append) { preparedProfileIcon = null; PlayerIcon.Source = null; }
        if (!append) { ProfileBody.Visibility = Visibility.Collapsed; AssetsText.Text = ""; }
        ProfileStatus.Text = "Chargement du profil…"; LoadingStage.Text = "Recherche du compte et des parties…";
        LoadingPanel.Visibility = append ? Visibility.Collapsed : Visibility.Visible; LoadingBar.IsIndeterminate = !append;
    }
    private void EndLoading()
    {
        MoreButton.IsEnabled = true; MoreButton.Content = "Voir plus · 10 parties"; QueueBox.IsEnabled = true;
        MoreButton.Visibility = profile is { HasMore: true, Demo: false } ? Visibility.Visible : Visibility.Collapsed;
        isPreparing = false; LoadingBar.IsIndeterminate = false; LoadingPanel.Visibility = Visibility.Collapsed;
        PersonalButton.IsEnabled = RefreshPersonalButton.IsEnabled = true;
        LoadButton.IsEnabled = DemoButton.IsEnabled = RiotIdBox.IsEnabled = ServerBox.IsEnabled = true;
        CancelButton.IsEnabled = false; SaveKeyButton.IsEnabled = ForgetKeyButton.IsEnabled = true;
    }
    private void RevealProfile(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (appending) appendScrollOffset = ProfileScroll.VerticalOffset;
        UpdateQueueFilters(); Render();
        ProfileBody.Visibility = Visibility.Visible;
    }
    private async void OnDemo(object sender, RoutedEventArgs e)
    {
        if (!loading.IsCompleted) return;
        browsingOther = true;
        BeginLoading(); profile = ProfileDemo.Create();
        loading = PrepareDemo(loadCancellation!.Token);
        try { await loading; } finally { EndLoading(); }
    }
    private async Task PrepareDemo(CancellationToken token)
    {
        try { await LoadAssets(token); RevealProfile(token); }
        catch (OperationCanceledException) { ProfileStatus.Text = "Chargement annulé."; }
    }
    private async Task LoadAssets(CancellationToken token, bool offlineOnly = false)
    {
        if (assets is null || profile is null) return;
        preparingImages = true;
        LoadingStage.Text = "Préparation des noms et illustrations…";
        if (!offlineOnly && queueCatalog is not null) await Task.Run(() => queueCatalog.PrepareAsync(token), token);
        await ProfileIcons.PrepareAsync(bitmaps, token);
        var matches = profile.Matches;
        var tiers = profile.Ranks.Select(r => r.Tier).Distinct().Where(t => new[] { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER" }.Contains(t)).ToArray();
        var rankImages = await Task.Run(() => tiers.ToDictionary(t => t, t => bitmaps.Get(Path.Combine(AppContext.BaseDirectory, "Assets", "Ranks", t + ".png"), 96)), token);
        foreach (var rankImage in rankImages) ranks[rankImage.Key] = rankImage.Value;
        foreach (var m in matches) { Visual(m.ChampionId, m.Champion, true); foreach (var id in m.Items) Visual(id, "", false); }
        var targets = visuals.Values.ToArray();
        using var updateGate = new SemaphoreSlim(1);
        await Task.Run(() => assets.PrepareAsync(matches, token, async () =>
        {
            await updateGate.WaitAsync(token);
            try
            {
                var updates = targets.Select(v => (Visual: v,
                    Name: v.IsChampion ? assets.ChampionName(v.Id, v.Code) : assets.ItemName(v.Id),
                    Icon: bitmaps.Get(v.IsChampion ? assets.ChampionImage(v.Id, v.Code) : assets.ItemImage(v.Id)))).ToArray();
                await Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    foreach (var update in updates) update.Visual.Update(update.Name, update.Icon);
                }, System.Windows.Threading.DispatcherPriority.Background, token);
            }
            finally { updateGate.Release(); }
        }, offlineOnly, profile.ProfileIconId), token);
        preparedProfileIcon = await Task.Run(() => bitmaps.Get(assets.ProfileIconImage(profile.ProfileIconId), 96), token);
        AssetsText.Text = assets.Notice;
    }
    private ProfileVisual Visual(int id, string code, bool champion)
    {
        var key = champion ? $"c:{id}:{code}" : $"i:{id}";
        if (!visuals.TryGetValue(key, out var visual)) visuals[key] = visual = new(id, code, champion);
        return visual;
    }
    private void UpdateQueueFilters()
    {
        var selected = QueueBox.SelectedValue is int id ? id : 0;
        var options = new Dictionary<int, string> { [0] = "Toutes les files" };
        foreach (var match in profile!.Matches.DistinctBy(m => m.Queue).OrderBy(m => m.QueueLabel)) options[match.Queue] = match.QueueLabel;
        QueueBox.ItemsSource = options; QueueBox.SelectedValue = options.ContainsKey(selected) ? selected : 0;
    }
    private void OnFilter(object sender, SelectionChangedEventArgs e) { if (profile is not null && !isPreparing) Render(); }
    private void Render()
    {
        if (profile is null) return;
        IdentityText.Text = $"{profile.RiotId}  ·  {RiotProfileClient.Platforms[profile.Platform].Label}";
        PlayerIcon.Source = preparedProfileIcon;
        PlayerIconFallback.Visibility = preparedProfileIcon is null ? Visibility.Visible : Visibility.Collapsed;
        LevelText.Text = $"Niveau {DisplayNumbers.Exact(profile.Level)}";
        ScopeText.Text = $"Niveau {DisplayNumbers.Exact(profile.Level)} · {profile.Matches.Count}/{profile.RequestedMatches} matchs disponibles · chargé à {profile.LoadedAt.ToLocalTime():HH:mm}";
        ProfileStatus.Text = (profile.Demo ? "DÉMONSTRATION — identité, rangs et matchs entièrement fictifs. " : $"Données Riot · {profile.RequestedMatches} dernières parties demandées. ") + profile.Notice;
        foreach (var m in profile.Matches) { Visual(m.ChampionId, m.Champion, true); foreach (var id in m.Items) Visual(id, "", false); }
        var filter = QueueBox.SelectedValue is int q ? q : 0;
        var selected = profile.Matches.Where(m => filter == 0 || m.Queue == filter).ToArray();
        var usable = selected.Where(m => !m.Remake && m.Seconds > 0).ToArray();
        var s = ProfileSummary.From(usable);
        SampleText.Text = s.Games == 0 ? "Aucune partie pour ce mode" : $"{s.Wins} victoires · {s.Games - s.Wins} défaites";
        MetricsPanel.Children.Clear(); ExtraMetricsPanel.Children.Clear();
        void Metric(string label, string value)
        {
            var key = label.StartsWith("Or") ? "gold" : label.StartsWith("CS") ? "cs" : label.StartsWith("Vision") ? "vision" :
                label.StartsWith("Balises contrôle") ? "controlward" : label.StartsWith("Balises") ? "ward" : label.StartsWith("Victoires") ? "win" : label.StartsWith("Morts") ? "death" :
                label.StartsWith("Assists") ? "assist" : label.StartsWith("Participation") ? "team" : "sword";
            var box = new StackPanel { Margin = new Thickness(0, 7, 12, 10) };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(ProfileIcons.Create(key, label, 24));
            header.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.LightSteelBlue, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 160, TextWrapping = TextWrapping.Wrap });
            box.Children.Add(header);
            box.Children.Add(new TextBlock { Text = s.Games == 0 ? "—" : value, FontSize = 22, Margin = new Thickness(0, 5, 0, 0) });
            if (label is "Victoires" or "KDA global" or "CS / min" or "Participation kills") MetricsPanel.Children.Add(box);
            else ExtraMetricsPanel.Children.Add(box);
        }
        Metric("Victoires", $"{s.WinRate:F1} %"); Metric("KDA global", $"{s.Kda:F2}"); Metric("Participation kills", s.Participation.HasValue ? $"{s.Participation:F1} %" : "—");
        Metric("CS / min", $"{s.CsPerMinute:F1}"); Metric("Dégâts champions / min", DisplayNumbers.Compact(s.DamagePerMinute)); Metric("Or / min", DisplayNumbers.Compact(s.GoldPerMinute));
        Metric("Kills / partie", $"{s.Kills:F1}"); Metric("Morts / partie", $"{s.Deaths:F1}"); Metric("Assists / partie", $"{s.Assists:F1}");
        Metric("Vision / partie", $"{s.Vision:F1}"); Metric("Balises posées / partie", $"{s.WardsPlaced:F1}"); Metric("Balises détruites / partie", $"{s.WardsKilled:F1}");
        Metric("Balises contrôle achetées", $"{s.ControlWards:F1} / partie");
        RankPanel.Children.Clear();
        RankLinks.Children.Clear();
        foreach (var queue in new[] { ("RANKED_SOLO_5x5", "Solo / Duo"), ("RANKED_FLEX_SR", "Flex") })
        {
            var rank = profile.Ranks.FirstOrDefault(r => r.Queue == queue.Item1);
            var card = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            card.Children.Add(new TextBlock { Text = queue.Item2, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 0, 0, 8) });
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            if (rank is not null && ranks.GetValueOrDefault(rank.Tier) is { } emblem)
                line.Children.Add(new Image { Source = emblem, Width = 60, Height = 60, Margin = new Thickness(0, 0, 8, 0) });
            var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            detail.Children.Add(new TextBlock { Text = rank is null ? "Non classé" : $"{TierLabel(rank.Tier)} {rank.Division}", FontSize = 17, FontWeight = FontWeights.SemiBold });
            if (rank is not null) detail.Children.Add(new TextBlock { Text = $"{DisplayNumbers.Exact(rank.Lp)} LP", Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 5, 0, 0) });
            line.Children.Add(detail); card.Children.Add(line);
            if (rank is not null) card.Children.Add(new TextBlock { Text = $"{rank.Wins} V · {rank.Losses} D   {(rank.Wins + rank.Losses == 0 ? 0 : 100.0 * rank.Wins / (rank.Wins + rank.Losses)):F1} %", Margin = new Thickness(0, 10, 0, 0), FontSize = 12 });
            RankPanel.Children.Add(card);
        }
        HistoryStatus.Text = profile.Demo ? "Historique fictif." : profile.HasMore ? "Chaque clic recherche 10 parties plus anciennes." : "Fin des parties disponibles auprès de Riot.";
        if (!profile.Demo && ProfileLinks.LeagueOfGraphs(profile.RiotId, profile.Platform) is { } rankingPage)
        {
            var link = new Button { Content = "Classement mondial ↗", HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Ouvre la fiche de ce joueur dans ton navigateur. Ces classements ne sont pas importés dans Rift Companion." };
            link.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rankingPage.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                { ShowNotice("Impossible d’ouvrir le navigateur pour cette fiche."); }
            };
            RankLinks.Children.Add(link);
        }
        RolePanel.Children.Clear();
        var roleMatches = usable.Where(m => QueueCatalog.HasStandardRoles(m.Queue)).ToArray();
        foreach (var group in roleMatches.GroupBy(m => m.RoleLabel).OrderByDescending(g => g.Count()))
        {
            var roleName = group.Key;
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(ProfileIcons.Create(group.First().Role, roleName, 24, "#85C9CE"));
            name.Children.Add(new TextBlock { Text = roleName, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(name);
            void AddBar(int column, double value, string label, string color, string tooltip)
            {
                var cell = new StackPanel { Margin = new Thickness(8, 0, 0, 0), ToolTip = tooltip };
                // Star columns keep the exact ratio at every window size, without animation or layout polling.
                var track = new Grid { Height = 7, Background = new SolidColorBrush(Color.FromRgb(42, 59, 72)) };
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - value, GridUnitType.Star) });
                track.Children.Add(new Border { Background = (Brush)new BrushConverter().ConvertFromString(color)! });
                cell.Children.Add(track);
                cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 5, 0, 0) });
                System.Windows.Automation.AutomationProperties.SetName(cell, tooltip);
                Grid.SetColumn(cell, column); row.Children.Add(cell);
            }
            var played = 100.0 * group.Count() / roleMatches.Length;
            var won = 100.0 * group.Count(m => m.Win) / group.Count();
            AddBar(1, played, $"{group.Count()} partie{(group.Count() > 1 ? "s" : "")}", "#35B3D5", $"{played:F1} % des parties avec rôles classiques du filtre actuel");
            AddBar(2, won, $"{won:F1} % V", "#32D9A0", $"{group.Count(m => m.Win)} victoires sur {group.Count()} parties en {roleName}");
            RolePanel.Children.Add(row);
        }
        if (roleMatches.Length == 0) RolePanel.Children.Add(new TextBlock { Text = "Aucune partie avec rôles classiques pour ce filtre.", TextWrapping = TextWrapping.Wrap });
        ChampionsGrid.ItemsSource = usable.GroupBy(m => m.ChampionId > 0 ? m.ChampionId.ToString() : m.Champion).OrderByDescending(g => g.Count()).Select(g =>
        { var stats = ProfileSummary.From(g); var m = g.First(); return new { ChampionVisual = Visual(m.ChampionId, m.Champion, true), Games = stats.Games, WinRate = $"{stats.WinRate:F0} %", Kda = $"{stats.Kda:F2}" }; }).ToArray();
        var wanted = selected.Select(m => m.Id).ToHashSet();
        for (int i = matchRows.Count - 1; i >= 0; i--) if (!wanted.Contains(matchRows[i].Match.Id)) matchRows.RemoveAt(i);
        for (int i = 0; i < selected.Length; i++)
        {
            var m = selected[i];
            var existing = matchRows.FirstOrDefault(r => r.Match.Id == m.Id);
            if (existing is null) matchRows.Insert(i, new(m, Visual(m.ChampionId, m.Champion, true), m.Items.Select(id => Visual(id, "", false)).ToArray()));
            else if (matchRows.IndexOf(existing) != i) matchRows.Move(matchRows.IndexOf(existing), i);
        }
    }
    private void ShowNotice(string message)
    {
        UserNotice.Text = message;
        NoticePanel.Visibility = Visibility.Visible;
    }
    private static string TierLabel(string tier) => tier switch { "IRON" => "Fer", "BRONZE" => "Bronze", "SILVER" => "Argent", "GOLD" => "Or", "PLATINUM" => "Platine", "EMERALD" => "Émeraude", "DIAMOND" => "Diamant", "MASTER" => "Maître", "GRANDMASTER" => "Grand maître", "CHALLENGER" => "Challenger", _ => tier };
    public async Task StopAsync()
    {
        stopping = true;
        await personalLifetime.CancelAsync();
        if (loadCancellation is not null) await loadCancellation.CancelAsync();
        try { await Task.WhenAll(loading, preferencesTask, startup, personalPolling); } catch (OperationCanceledException) { }
        ApiKeyBox.Clear(); loadCancellation?.Dispose();
        await Task.Run(() => { api.Dispose(); assets?.Dispose(); queueCatalog?.Dispose(); personalDetector?.Dispose(); });
        personalLifetime.Dispose();
    }
}
