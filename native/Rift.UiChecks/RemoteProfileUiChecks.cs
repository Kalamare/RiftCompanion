using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Rift.Contracts;
using Rift.Core;
using Rift.Desktop;

static class RemoteProfileUiChecks
{
    public static async Task Run(string folder)
    {
        var oldUrl = Environment.GetEnvironmentVariable("RIFT_SERVER_URL");
        var oldKey = Environment.GetEnvironmentVariable("RIFT_SERVER_ACCESS_KEY_FILE");
        var keyFile = Path.Combine(folder, "private-server-test-key"); File.WriteAllText(keyFile, new string('x', 32));
        Environment.SetEnvironmentVariable("RIFT_SERVER_URL", "http://127.0.0.1:1/");
        Environment.SetEnvironmentVariable("RIFT_SERVER_ACCESS_KEY_FILE", keyFile);
        var view = new ProfileView();
        try
        {
            view.Configure(Path.Combine(folder, "remote-ui"));
            T Control<T>(string name) => (T)view.FindName(name);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            typeof(ProfileView).GetMethod("ShowPendingProfile", flags)!.Invoke(view, ["Attente#TEST", "euw1"]);
            if (Control<FrameworkElement>("ProfileBody").Visibility != Visibility.Visible || Control<FrameworkElement>("LoadingPanel").Visibility != Visibility.Collapsed ||
                !Control<TextBlock>("SampleText").Text.Contains("Chargement") || Control<ContentControl>("WinrateChart").Content is not null ||
                Control<TextBlock>("HistoryLoadingText").Visibility != Visibility.Visible)
                throw new Exception("Unknown profile must display partial cards without invented zero statistics");
            var first = ProfileDemo.Create().Matches[0] with { Id = "sample-1", Queue = 420, ChampionId = 103, Role = "middle", Kills = 6, Deaths = 2, Assists = 4, Remake = false, Seconds = 600 };
            var second = first with { Id = "sample-2", Queue = 450, ChampionId = 1, Kills = 0, Deaths = 4, Assists = 2 };
            var profile = new PlayerProfile("Serveur#TEST", "euw1", 100, [], [first, second], DateTimeOffset.UtcNow, false, 20) { Puuid = "test-player" };
            typeof(ProfileView).GetField("profile", flags)!.SetValue(view, profile);
            void Render() => typeof(ProfileView).GetMethod("Render", flags)!.Invoke(view, null);
            string Kda() => ((TextBlock)((StackPanel)Control<System.Windows.Controls.Primitives.UniformGrid>("MetricsPanel").Children[1]).Children[1]).Text;
            Render();
            if (Control<Expander>("StatisticsSettings").Visibility != Visibility.Collapsed) throw new Exception("Season settings visible in sample mode");
            if (!Control<TextBlock>("SampleText").Text.StartsWith("2 parties") || Kda() != 2.0.ToString("F2") || Control<ListBox>("ChampionsGrid").Items.Count != 2)
                throw new Exception("Displayed sample totals or weighted KDA incorrect");
            typeof(ProfileView).GetMethod("SelectCommonMode", flags)!.Invoke(view, [420]);
            if (!Control<TextBlock>("SampleText").Text.StartsWith("1 parties") || Kda() != 5.0.ToString("F2") || Control<ListBox>("ChampionsGrid").Items.Count != 1)
                throw new Exception("Common mode did not filter overview and champions together");
            if (new[] { "SummaryMode", "RolesMode", "ChampionsMode", "QueueBox" }.Any(n => Control<ComboBox>(n).Visibility != Visibility.Collapsed))
                throw new Exception("Repeated filters still visible");
            typeof(ProfileView).GetMethod("SelectCommonMode", flags)!.Invoke(view, [0]);
            profile = profile with { Matches = [first, second, first with { Id = "sample-3", Kills = 12, Deaths = 0, Assists = 0 }] };
            typeof(ProfileView).GetField("profile", flags)!.SetValue(view, profile); Render();
            if (!Control<TextBlock>("SampleText").Text.StartsWith("3 parties") || Kda() != 4.0.ToString("F2")) throw new Exception("Pagination did not expand sample statistics");
            if (!Control<TextBlock>("ChampionsScope").Text.Contains("pas un bilan de saison")) throw new Exception("Sample scope not explicit");
            var previous = profile;
            profile = profile with { Matches = Enumerable.Range(0, 30).Select(i => first with { Id = "limit-" + i, PlayedAt = DateTimeOffset.UtcNow.AddMinutes(-i), ChampionId = i < 20 ? 10 + i % 4 : 999 }).ToArray() };
            typeof(ProfileView).GetField("profile", flags)!.SetValue(view, profile); Render();
            var top = Control<ListBox>("ChampionsGrid").Items.Cast<object>().ToArray();
            if (top.Length != 3 || top.Any(r => ((ProfileVisual)r.GetType().GetProperty("ChampionVisual")!.GetValue(r)!).Id == 999))
                throw new Exception("Top champions must stop at three and ignore matches older than the latest 20");
            var snapshot = new StatsSnapshot(profile.Platform, profile.Puuid, new DateTimeOffset(2026,1,8,0,0,0,TimeSpan.Zero), DateTimeOffset.UtcNow, -1,
                ProfileSummary.From(profile.Matches), Enumerable.Range(1, 8).Select(i => new ChampionStats(i, "Champion" + i, ProfileSummary.From([first]))).ToArray(), [], 3, new(null, null, false, 0, "limited"));
            typeof(ProfileView).GetField("sidebarSeason", flags)!.SetValue(view, snapshot); Render();
            if (Control<ItemsControl>("SidebarChampions").Items.Count != 6 || Control<Button>("MoreSeasonChampions").Visibility != Visibility.Visible ||
                !Control<TextBlock>("SidebarSeasonScope").Text.Contains("partiel")) throw new Exception("Season sidebar must show cached totals and partial coverage");
            Control<Button>("MoreSeasonChampions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (Control<ItemsControl>("SidebarChampions").Items.Count != 8) throw new Exception("Season list expansion lost stored champions");
            if (Control<StackPanel>("RankPanel").Children.OfType<Expander>().Any()) throw new Exception("Flex must be visible without expansion");
            profile = previous;
            typeof(ProfileView).GetField("profile", flags)!.SetValue(view, profile); Render();
            if (Environment.GetEnvironmentVariable("RIFT_UI_PREVIEW_DIR") is { } preview)
            {
                await (Task)typeof(ProfileView).GetMethod("LoadAssets", flags)!.Invoke(view, [CancellationToken.None, true])!;
                Render();
                Control<StackPanel>("ProfileBody").Visibility = Visibility.Visible;
                Directory.CreateDirectory(preview);
                foreach (var width in new[] { 1050, 1220, 1920 })
                {
                    view.Width = width; view.Height = 1000;
                    view.Measure(new Size(width, 1000)); view.Arrange(new Rect(0, 0, width, 1000)); view.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 1000, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(preview, $"server-profile-{width}.png")); png.Save(file);
                }
            }
            typeof(ProfileView).GetField("personalProfile", flags)!.SetValue(view, profile with { RiotId = "Moi#TEST", Puuid = "personal" });
            typeof(ProfileView).GetField("browsingOther", flags)!.SetValue(view, true);
            Control<TextBox>("RiotIdBox").Text = "TexteNonValide";
            typeof(ProfileView).GetMethod("OnPersonalProfile", flags)!.Invoke(view, [Control<Button>("RefreshPersonalButton"), new RoutedEventArgs()]);
            await (Task)typeof(ProfileView).GetField("loading", flags)!.GetValue(view)!;
            if (Control<TextBox>("RiotIdBox").Text != profile.RiotId || !(bool)typeof(ProfileView).GetField("browsingOther", flags)!.GetValue(view)! ||
                ((PlayerProfile?)typeof(ProfileView).GetField("profile", flags)!.GetValue(view))?.Puuid != profile.Puuid)
                throw new Exception("Refresh navigated to personal account or lost current profile on failure");
            var serverNow = DateTimeOffset.UtcNow.AddHours(2);
            typeof(ProfileView).GetField("refreshStatus", flags)!.SetValue(view, new RefreshStatus("cooldown", serverNow, serverNow.AddMinutes(-1), serverNow.AddMinutes(5)));
            typeof(ProfileView).GetField("refreshReceivedAt", flags)!.SetValue(view, DateTimeOffset.UtcNow);
            typeof(ProfileView).GetMethod("RenderRefreshStatus", flags)!.Invoke(view, null);
            if (Control<Button>("RefreshPersonalButton").IsEnabled || !Control<Button>("RefreshPersonalButton").Content.ToString()!.Contains("04:"))
                throw new Exception("Cooldown did not use server time or failed to disable refresh");
            typeof(ProfileView).GetField("refreshStatus", flags)!.SetValue(view, new RefreshStatus("ready", serverNow, null, serverNow.AddSeconds(-1)));
            typeof(ProfileView).GetMethod("RenderRefreshStatus", flags)!.Invoke(view, null);
            if (!Control<Button>("RefreshPersonalButton").IsEnabled) throw new Exception("Expired cooldown still disables refresh");
            Console.WriteLine("OK WPF : mode serveur sans clé Riot ni synchronisation, agrégats indépendants de l’historique récent, filtres distincts et couverture partielle explicite.");
        }
        finally
        {
            await view.StopAsync();
            Environment.SetEnvironmentVariable("RIFT_SERVER_URL", oldUrl);
            Environment.SetEnvironmentVariable("RIFT_SERVER_ACCESS_KEY_FILE", oldKey);
        }
    }
}
