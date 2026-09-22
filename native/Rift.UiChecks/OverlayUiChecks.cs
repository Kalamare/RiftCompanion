using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rift.Desktop;
using Rift.Core;

static class OverlayUiChecks
{
    public static async Task Run(string directory)
    {
        OverlayKeyboardChecks.Run();
        var banner = new OverlayBanner { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual };
        bool opened = false;
        banner.OpenRequested += () => opened = true;
        try
        {
            banner.Show(); banner.UpdateLayout();
            if (banner.ShowActivated || banner.ShowInTaskbar || !banner.Topmost) throw new Exception("Banner focus/taskbar contract failed");
            var preview = Environment.GetEnvironmentVariable("RIFT_UI_PREVIEW_DIR");
            if (preview is not null)
            {
                var bitmap = new RenderTargetBitmap(340, 76, 96, 96, PixelFormats.Pbgra32); bitmap.Render(banner);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(preview, "overlay-banner.png")); png.Save(file);
            }
            banner.OpenButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (!opened || banner.IsVisible) throw new Exception("Banner opening did not dismiss reminder");
            opened = false; banner.Show();
            banner.DismissButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (opened || banner.IsVisible) throw new Exception("Banner dismiss opened overlay");
            Console.WriteLine("OK WPF : bannière overlay sans activation, ouverture et fermeture distinctes.");
        }
        finally { banner.Close(); }
        int requests = 0; bool cancelled = false;
        var begun = new TaskCompletionSource();
        var profile = ProfileDemo.Create();
        var view = new OverlayWindow(directory, async (id, _, ct) =>
        {
            if (id.EndsWith("#BOT")) throw new Exception("Bot requested Riot profile");
            if (id == "Missing#EUW") throw new Rift.Infrastructure.RiotApiException("Absent", playerUnavailable: true);
            begun.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { cancelled = true; throw; }
            return profile;
        }, _ => Task.FromResult<PlayerProfile?>(profile), _ =>
        {
            requests++;
            return Task.FromResult<IReadOnlyList<OverlayPlayer>>([new("Udyr#BOT", "ORDER", "Udyr", 1, "top", 8112, []), new("Missing#EUW", "ORDER", "Annie", 1, "jungle", 8112, []), new("Visible#EUW", "ORDER", "Ahri", 1, "middle", 8112, ["SummonerFlash"])]);
        }) { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            view.Show(); await begun.Task.WaitAsync(TimeSpan.FromSeconds(5)); view.Hide();
            await view.Monitoring.WaitAsync(TimeSpan.FromSeconds(5));
            if (!cancelled || requests != 1) throw new Exception("Overlay hidden still loading");
            var retained = view.Players.ItemsSource;
            begun = new TaskCompletionSource();
            view.Show(); await begun.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!ReferenceEquals(retained, view.Players.ItemsSource)) throw new Exception("Unchanged roster recreated on reopen");
            view.Hide(); await view.Monitoring.WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine("OK overlay : bots sans requête, joueur absent isolé, mêmes cartes conservées à la réouverture.");
            await view.ShowExample();
            if (view.Players.Items.Count != 10 || !view.Status.Text.Contains("FICTIVE") || requests != 2) throw new Exception("Overlay demo uses network or misses players");
            var firstCard = (OverlayCard)view.Players.Items[0];
            var sampleOpgg = new OpggProfile(firstCard.Name, "euw1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), 120, 10000, "RANKED", 33,
                [new OpggChampion(516, "Ornn", 50, 30, 250, 100, 500)]);
            firstCard.ApplyOpgg(sampleOpgg);
            firstCard.Apply(profile, null, null); // Riot completion must not erase independent OP.GG results.
            if (!firstCard.ServerRank.Contains("120") || firstCard.Kills != 5.0.ToString("F1") || !firstCard.ChampionStats.Contains("50 parties") || !firstCard.OpggDetails.Contains("profil source")) throw new Exception("OP.GG overlay attribution or independent completion failed");
            var profileView = new ProfileView();
            profileView.Configure(directory);
            await profileView.OpenParticipantProfile(ProfileDemo.Details(profile.Matches[1]).Participants[0], "euw1", true);
            sampleOpgg = sampleOpgg with { SeasonGames = 692, SeasonWins = 353, SeasonLosses = 339 };
            await profileView.PrepareSeasonImages(sampleOpgg, default);
            profileView.ShowOpgg(sampleOpgg);
            if (!profileView.OpggRank.Text.Contains("120") || profileView.OpggChampions.Items.Count != 1 || profileView.OpggScope.Text.Contains("OP.GG")) throw new Exception("OP.GG profile panel failed");
            if (!profileView.SampleText.Text.Contains("692") || profileView.ChampionsGrid.Items.Count != 1 || profileView.RolePanel.Children.Count != 1) throw new Exception("Season panels use recent matches");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var selectedProfile = (PlayerProfile)typeof(ProfileView).GetField("profile", flags)!.GetValue(profileView)!;
            var from = new DateTimeOffset(DateTime.SpecifyKind(profileView.StatsFrom.SelectedDate!.Value, DateTimeKind.Utc));
            var seasonalMatches = profile.Matches.Take(4).Select((m, i) => m with { Id = "SEASON_" + i, Queue = i < 2 ? 420 : i == 2 ? 440 : 450,
                Remake = false, Role = i < 2 ? "jungle" : "bottom", PlayedAt = from.AddDays(i + 1) }).ToArray();
            typeof(ProfileView).GetField("seasonHistory", flags)!.SetValue(profileView,
                new SeasonHistory(selectedProfile.Platform, selectedProfile.Puuid, from, DateTimeOffset.UtcNow, 4, true, 0, seasonalMatches));
            profileView.SummaryMode.SelectedValue = 420;
            profileView.RolesMode.SelectedValue = 440;
            profileView.ChampionsMode.SelectedValue = 450;
            if (!profileView.SampleText.Text.StartsWith("2 parties") || profileView.RolePanel.Children.Count != 1 || profileView.ChampionsGrid.Items.Count != 1)
                throw new Exception("Independent season modes failed");
            profileView.SummaryMode.SelectedValue = 440;
            if (!profileView.SampleText.Text.StartsWith("1 parties") || (int)profileView.ChampionsMode.SelectedValue != 450 || (int)profileView.RolesMode.SelectedValue != 440)
                throw new Exception("Changing summary mode modified another panel");
            profileView.RolesMode.SelectedValue = 450;
            if (profileView.RolePanel.Children.OfType<System.Windows.Controls.TextBlock>().Single().Text != "Aucune partie avec rôle standard pour ce mode.")
                throw new Exception("ARAM role inferred from champion");
            profileView.SummaryMode.SelectedValue = profileView.RolesMode.SelectedValue = profileView.ChampionsMode.SelectedValue = 0;
            if (profileView.SeasonScope.Text.Contains("OP.GG") || profileView.ChampionsScope.Text.Contains("OP.GG") || firstCard.ServerRank.Contains("OP.GG"))
                throw new Exception("Provider branding remains in player UI");
            Console.WriteLine("OK WPF : filtres saisonniers indépendants, ARAM sans rôle inventé, agrégats calculés et interface sans marque fournisseur.");
            var opggPreview = Environment.GetEnvironmentVariable("RIFT_UI_PREVIEW_DIR");
            if (opggPreview is not null)
            {
                var previewWindow = new Window { Content = profileView, Width = 1220, Height = 1000, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                previewWindow.Show(); previewWindow.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1220, 1000, 96, 96, PixelFormats.Pbgra32); bitmap.Render(previewWindow);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(opggPreview, "opgg-profile.png")); png.Save(file);
                previewWindow.Close();
            }
            await profileView.StopAsync();
            Console.WriteLine("OK OP.GG WPF : rang et saison attribués, dates visibles, enrichissements Riot/OP.GG indépendants.");
            view.UpdateLayout();
            var output = Environment.GetEnvironmentVariable("RIFT_UI_PREVIEW_DIR");
            if (output is not null)
            {
                var bitmap = new RenderTargetBitmap(1280, 830, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "overlay.png")); png.Save(file);
            }
            Console.WriteLine("OK WPF : overlay dix joueurs, démonstration sans réseau, masquage annule le chargement et arrête les lectures.");
        }
        finally { await view.StopAsync(); }
    }
}
