using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rift.Desktop;
using Rift.Core;
using Rift.Infrastructure;

static class MatchDetailsUiChecks
{
    public static async Task Run()
    {
        var match = ProfileDemo.Create().Matches[1];
        var presentation = new MatchDetailsPresentation(ProfileDemo.Details(match), "demo-selected", true);
        if (presentation.Teams.Count != 2 || presentation.Teams.Any(t => t.Players.Count != 5) ||
            presentation.Teams.SelectMany(t => t.Players).Count(p => p.IsSelectedPlayer) != 1 ||
            presentation.Teams.SelectMany(t => t.Players).Any(p => p.Items.Count != 7)) throw new Exception("Incomplete match scoreboard");
        var view = new MatchDetailsView { DataContext = presentation };
        using var assets = new RiotAssets(Path.GetTempPath(), bundledDirectory: Path.Combine(AppContext.BaseDirectory, "Assets", "Champions"));
        var cache = new ProfileBitmapCache();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var updates = await Task.Run(() => presentation.Visuals.Where(v => v.IsChampion || v.IsSpell || v.IsRune).Select(v => (Visual: v,
            Name: v.IsRune ? assets.RuneName(v.Id) : v.IsSpell ? assets.SpellName(v.Id) : assets.ChampionName(v.Id, v.Code),
            Description: v.IsRune ? assets.RuneDescription(v.Id) : v.IsSpell ? assets.SpellDescription(v.Id) : "",
            Image: cache.Get(v.IsRune ? assets.RuneImage(v.Id) : v.IsSpell ? assets.SpellImage(v.Id) : assets.ChampionImage(v.Id, v.Code), v.IsChampion ? 120 : 64))).ToArray());
        foreach (var update in updates) update.Visual.Update(update.Name, update.Image, update.Description);
        if (updates.Any(u => u.Image is null || !u.Image.IsFrozen) || presentation.Teams.SelectMany(t => t.Players).Any(p => p.Spells.Count != 2)) throw new Exception("Bundled portraits/spells unavailable offline");
        Console.WriteLine($"OK WPF : portraits et sorts livrés, décodage hors UI en {watch.ElapsedMilliseconds} ms sans réseau.");
        if (updates.Where(u => u.Visual.IsChampion).Any(u => ((BitmapSource)u.Image!).PixelWidth != 120) ||
            updates.Where(u => u.Visual.IsSpell || u.Visual.IsRune).Any(u => u.Description.Length < 10 || u.Description.Contains("<br")))
            throw new Exception("Portrait resolution or offline descriptions missing");
        var stats = new MatchStatistics(match with { Seconds = 1200, Cs = 100, Gold = 10000, Vision = 20 });
        if (!stats.Farming.Contains((5.0).ToString("F1")) || !stats.Farming.Contains((500.0).ToString("F1")) || !stats.Vision.Contains((1.0).ToString("F1"))) throw new Exception("Incorrect per-minute statistics");
        if (!new MatchStatistics(match with { Seconds = 0 }).Farming.Contains("—")) throw new Exception("Zero duration not handled");
        var jungler = presentation.Teams.SelectMany(t => t.Players).First(p => p.RoleQuest.Id == 1103);
        if (!jungler.EquipmentItems.Select(v => v.Id).SequenceEqual(jungler.Player.Stats.Items.Take(6)) || jungler.Trinket.Id != jungler.Player.Stats.Items[6] ||
            jungler.Runes[0].Id != 8005 || jungler.Runes.Count != 1 || jungler.SecondaryRunes.Count != 2 || jungler.FullRunes.Count != 6 || !presentation.AssetMatches.Any(m => m.Items.Contains(1103)))
            throw new Exception("Compact inventory or rune order incorrect");
        Console.WriteLine("OK WPF : six objets séparés de la balise/quête et runes détaillées disponibles sans réseau.");
        var ranks = new PlayerRanks([new("RANKED_SOLO_5x5", "GOLD", "II", 32, 3, 2), new("RANKED_FLEX_SR", "DIAMOND", "IV", 10, 4, 1)], DateTimeOffset.UtcNow);
        var rankIcon = await Task.Run(() => cache.Get(Path.Combine(AppContext.BaseDirectory, "Assets", "Ranks", "GOLD.png")));
        if (rankIcon is null || !rankIcon.IsFrozen) throw new Exception("Bundled rank icon missing");
        foreach (var player in presentation.Teams.SelectMany(t => t.Players)) player.SetRank(ranks, 420, rankIcon, true);
        var selected = presentation.Teams[0].Players[0];
        selected.SetRank(ranks, 440, null);
        if (!selected.Rank.Name.Contains("Diamant") || !selected.Rank.Name.StartsWith("Flex")) throw new Exception("Wrong queue rank");
        selected.SetRank(null, 420, null);
        if (!selected.Rank.Name.Contains("indisponible")) throw new Exception("Unavailable rank reported as unranked");
        selected.SetRank(ranks, 420, rankIcon, true);
        foreach (var goal in new[] { "tower", "dragon", "baron", "riftHerald", "inhibitor", "horde" })
            if (!ProfileIcons.Create(goal, goal).Source.IsFrozen) throw new Exception("Invalid objective pictogram");
        if (presentation.Teams.SelectMany(t => t.Objectives).Any(o => !o.Icon.IsFrozen)) throw new Exception("Unfrozen objective icon");
        Console.WriteLine("OK WPF : icônes d’objectifs locales, rang Solo/Flex distinct et indisponibilité explicite.");
        var output = Environment.GetEnvironmentVariable("RIFT_UI_PREVIEW_DIR");
        foreach (int width in new[] { 960, 1220, 1600 })
        {
            view.Width = width; view.Height = 1100;
            view.Measure(new Size(width, 1100)); view.Arrange(new Rect(0, 0, width, 1100)); view.UpdateLayout();
            if (view.DetailScroll.ExtentWidth > view.DetailScroll.ViewportWidth + 1) throw new Exception("Match detail horizontal overflow");
            if (output is null) continue;
            Directory.CreateDirectory(output);
            var bitmap = new RenderTargetBitmap(width, 1100, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"match-details-{width}.png")); png.Save(file);
        }
        Console.WriteLine("OK WPF : détail de partie, deux équipes, dix joueurs, joueur du profil surligné et rendu 960/1220/1600 px.");
        var runeHost = new Window { Content = view, Width = 1600, Height = 1100, Left = -10000, Top = -10000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        runeHost.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var runeButton = Descendants(view).OfType<System.Windows.Controls.Button>().First(b => b.Content is System.Windows.Controls.ItemsControl && b.DataContext is DetailPlayerRow);
        jungler.PrimaryRunes[0].Update(jungler.PrimaryRunes[0].Name, jungler.PrimaryRunes[0].Icon, string.Join("\n", Enumerable.Repeat(jungler.PrimaryRunes[0].Description, 5)));
        runeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        view.RuneScroll.UpdateLayout();
        var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent };
        view.RuneScroll.RaiseEvent(wheel); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); view.RuneScroll.UpdateLayout();
        if (!view.RunesPopup.IsOpen || !wheel.Handled || view.RuneScroll.ScrollableHeight <= 0 || view.RuneScroll.VerticalOffset <= 0) throw new Exception($"Rune popup: open {view.RunesPopup.IsOpen}, handled {wheel.Handled}, extent {view.RuneScroll.ScrollableHeight}, offset {view.RuneScroll.VerticalOffset}");
        if (!Descendants(view.RunesPopup.Child).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == jungler.SecondaryRunes[1].Name)) throw new Exception("Second secondary rune missing from popup");
        runeButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
        await Task.Delay(350);
        if (view.RunesPopup.PlacementTarget?.IsMouseOver != true && !view.RunesPopup.Child.IsMouseOver && view.RunesPopup.IsOpen) throw new Exception("Rune popup did not close after hover leave");
        view.RunesPopup.IsOpen = false; runeHost.Content = null; runeHost.Close();
        var recipeVisual = new ProfileVisual(3074, "", false);
        recipeVisual.Update("Hydre vorace", jungler.Champion.Icon, "+65 dégâts d’attaque\nEffet de zone sur les ennemis proches.");
        recipeVisual.UpdateRecipe(3300, [new("Composant A", jungler.Champion.Icon), new("Composant B", jungler.Champion.Icon)]);
        var itemTip = new System.Windows.Controls.ToolTip { Content = recipeVisual };
        var statsTip = new System.Windows.Controls.ToolTip { Content = jungler.Statistics };
        var spellTip = new System.Windows.Controls.ToolTip { Content = jungler.Spells[0] };
        foreach (var (tip, name) in new[] { (statsTip, "statistics"), (spellTip, "spell"), (itemTip, "item") })
        {
            tip.Measure(new Size(700, 650)); tip.Arrange(new Rect(tip.DesiredSize)); tip.UpdateLayout();
            if (!Descendants(tip).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text.Length > 40)) throw new Exception("Rich tooltip has no description: " + name);
            if (output is not null)
            {
                // ToolTip itself must remain detached; host its actual content for a full ScrollViewer render.
                var content = tip.Content; tip.Content = null;
                var preview = new System.Windows.Controls.Border { Background = tip.Background, Padding = tip.Padding,
                    DataContext = tip.DataContext, Child = new System.Windows.Controls.ContentPresenter { Content = content },
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                System.Windows.Controls.TextBlock.SetForeground(preview, tip.Foreground);
                var host = new Window { Content = preview, Width = 700, Height = 650, Left = -10000, Top = -10000,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                try
                {
                host.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded); host.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(preview.ActualWidth), (int)Math.Ceiling(preview.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"tooltip-{name}.png")); png.Save(file);
                }
                finally { host.Close(); preview.Child = null; }
            }

        }
        Console.WriteLine("OK WPF : infobulles runes/sorts/statistiques rendues avec descriptions et seconde rune secondaire.");
        MatchParticipant? clicked = null;
        view.PlayerRequested += player => clicked = player;
        var links = Descendants(view).OfType<System.Windows.Controls.Button>().Where(b => b.DataContext is DetailPlayerRow && b.Content is not System.Windows.Controls.ItemsControl).ToArray();
        if (links.Length != 20) throw new Exception("Missing player name or portrait links");
        foreach (var link in links)
        {
            if (link.Content is FrameworkElement content && link.ActualWidth > content.ActualWidth + 1) throw new Exception("Player hover extends beyond its content");
            clicked = null;
            link.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (clicked?.Puuid != ((DetailPlayerRow)link.DataContext).Player.Puuid) throw new Exception("Wrong player navigation target");
        }
        Console.WriteLine("OK WPF : noms et portraits des dix joueurs ouvrent la bonne identité.");
        var diagnostics = new DiagnosticsWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            diagnostics.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var diagnosticDeadline = DateTime.UtcNow.AddSeconds(5);
            while (diagnostics.Metrics.Text.Length == 0 && DateTime.UtcNow < diagnosticDeadline) await Task.Delay(10);
            if (diagnostics.AnalysisStatus.Text.Length == 0 || !diagnostics.Metrics.Text.Contains("CPU") || diagnostics.ThreadList.Items.Count == 0 || diagnostics.RecentList.Items.Count == 0) throw new Exception("Diagnostics metrics, threads or activity not populated");
            Descendants(diagnostics).OfType<System.Windows.Controls.TabControl>().First().SelectedIndex = 0;
            if (!diagnostics.SamplingEnabled) throw new Exception("Diagnostics sampling not started");
            if (output is not null)
            {
                diagnostics.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1120, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(diagnostics);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "diagnostics.png")); png.Save(file);
            }
        }
        finally { diagnostics.Close(); }
        if (diagnostics.SamplingEnabled) throw new Exception("Diagnostics sampling continues after closing");
        Console.WriteLine("OK WPF : fenêtre diagnostic et arrêt des relevés à la fermeture.");
        var started = new TaskCompletionSource(); bool cancelled = false;
        var window = new MatchDetailsWindow(async token =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return ProfileDemo.Details(match);
        }, Path.GetTempPath(), "demo-selected", true)
        { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.SetStatus("Illustrations : test", diagnostic: true);
        if (window.Status.Visibility != Visibility.Collapsed) throw new Exception("Illustrations visible without debug");
        window.DebugEnabled = true;
        if (window.Status.Visibility != Visibility.Visible) throw new Exception("Illustrations hidden in debug");
        window.DebugEnabled = false;
        window.SetStatus("Erreur de chargement");
        if (window.Status.Visibility != Visibility.Visible) throw new Exception("Error hidden without debug");
        Console.WriteLine("OK WPF : illustrations réservées au Débug, erreurs toujours visibles.");
        try
        {
            window.Show(); await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            window.Close(); await window.Loading.WaitAsync(TimeSpan.FromSeconds(3));
            if (!cancelled) throw new Exception("Closing detail window did not cancel the request");
        }
        finally { if (window.IsVisible) window.Close(); }
        Console.WriteLine("OK WPF : fermeture du détail annule immédiatement le chargement en cours.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
