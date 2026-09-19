using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Rift.Core;
using Rift.Desktop;
using Rift.Infrastructure;

static class PersonalProfileUiChecks
{
    public static async Task Run(string folder)
    {
        var directory = Path.Combine(folder, "personal-ui");
        var owner = new PlayerProfile("Personnel#TEST", "euw1", 50, [], [], DateTimeOffset.UtcNow, false, 20) { ProfileIconId = 685 };
        var assets = Path.Combine(directory, "riot-assets");
        var version = Path.Combine(assets, "16.18.1");
        Directory.CreateDirectory(Path.Combine(version, "profileicon"));
        File.WriteAllText(Path.Combine(assets, "versions.json"), "[\"16.18.1\"]");
        File.WriteAllText(Path.Combine(version, "champion-fr.json"), "{\"data\":{}}");
        File.WriteAllText(Path.Combine(version, "item-fr.json"), "{\"data\":{}}");
        File.Copy(Path.Combine(folder, "icon.png"), Path.Combine(version, "profileicon", "685.png"));
        new PersonalProfileStore(directory).Save(owner);
        var view = new ProfileView();
        view.Configure(directory);
        view.StartPersonalProfiles(_ => Task.FromResult<Credentials?>(null), true);
        T Control<T>(string name) => (T)view.FindName(name);
        async Task WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition()) { if (DateTime.UtcNow > deadline) throw new Exception("UI personal profile timeout"); await Task.Delay(10); }
        }
        try
        {
            await WaitFor(() => Control<StackPanel>("ProfileBody").Visibility == Visibility.Visible && Control<Button>("PersonalButton").IsEnabled);
            if (!Control<TextBlock>("IdentityText").Text.Contains(owner.RiotId) || !Control<TextBlock>("ProfileStatus").Text.Contains("enregistré"))
                throw new Exception("Offline startup did not restore owner");
            Console.WriteLine("OK WPF : démarrage sans LoL ni clé, profil personnel restauré et daté.");
            if (Control<Image>("PlayerIcon").Source is not System.Windows.Media.Imaging.BitmapSource icon || !icon.IsFrozen || icon.PixelWidth != 96 ||
                Control<TextBlock>("PlayerIconFallback").Visibility != Visibility.Collapsed)
                throw new Exception("Profile icon missing, not frozen or wrong decoding size");
            Console.WriteLine("OK WPF : icône du joueur restaurée hors ligne, décodée à 96 px et gelée.");
            // A manual lookup with no key fails locally; it must not change ownership.
            Control<TextBox>("RiotIdBox").Text = "Autre#TEST";
            Control<Button>("LoadButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => Control<Button>("LoadButton").IsEnabled);
            if (Control<Image>("PlayerIcon").Source is not null) throw new Exception("Previous player's icon retained after search");
            if (new PersonalProfileStore(directory).Read()!.RiotId != owner.RiotId) throw new Exception("Search replaced owner");
            Control<Button>("PersonalButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => Control<Button>("PersonalButton").IsEnabled);
            if (!Control<TextBlock>("IdentityText").Text.Contains(owner.RiotId) || Control<StackPanel>("ProfileBody").Visibility != Visibility.Visible)
                throw new Exception("Return to own profile failed");
            Console.WriteLine("OK WPF : recherche indépendante puis retour Mon profil sans réseau.");
            // Inject a failing local refresh, retaining the already displayed snapshot.
            Control<PasswordBox>("ApiKeyBox").Password = "invalid key with spaces";
            Control<Button>("RefreshPersonalButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => Control<Button>("RefreshPersonalButton").IsEnabled);
            var backing = (PlayerProfile?)typeof(ProfileView).GetField("profile", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view);
            if (backing?.RiotId != owner.RiotId || Control<StackPanel>("ProfileBody").Visibility != Visibility.Visible)
                throw new Exception("Refresh error erased saved profile");
            Console.WriteLine("OK WPF : échec d’actualisation conserve le profil personnel affiché.");
            if (Control<Border>("NoticePanel").Visibility != Visibility.Visible) throw new Exception("Actionable error hidden behind debug");
            var debug = Control<System.Windows.Controls.Primitives.ToggleButton>("DebugButton");
            if (Control<Border>("DebugPanel").Visibility != Visibility.Collapsed) throw new Exception("Debug visible by default");
            debug.IsChecked = true;
            await Task.Delay(10);
            if (Control<Border>("DebugPanel").Visibility != Visibility.Visible) throw new Exception("Debug toggle failed");
            debug.IsChecked = false;
            Console.WriteLine("OK WPF : diagnostic masqué par défaut, bouton Débug fonctionnel et erreurs toujours visibles.");

            // Render the real control with synthetic data, without starting a window or network request.
            var sample = ProfileDemo.Create();
            typeof(ProfileView).GetField("profile", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, sample);
            await (Task)typeof(ProfileView).GetMethod("LoadAssets", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [CancellationToken.None, true])!;
            typeof(ProfileView).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(view, null);
            Control<Border>("NoticePanel").Visibility = Visibility.Collapsed;
            if (Control<System.Windows.Controls.Primitives.UniformGrid>("MetricsPanel").Children.Count != 4 || Control<System.Windows.Controls.Primitives.UniformGrid>("RankPanel").Children.Count != 2)
                throw new Exception("Compact metrics or rank cards missing");
            var roleRows = Control<StackPanel>("RolePanel").Children.Cast<Grid>().ToArray();
            double BarValue(Grid row, int column) => ((Grid)((StackPanel)row.Children[column]).Children[0]).ColumnDefinitions[0].Width.Value;
            if (roleRows.Length != 5 || Math.Abs(BarValue(roleRows[0], 1) - 100.0 * 8 / 12) > 0.001 ||
                Math.Abs(BarValue(roleRows[0], 2) - 75) > 0.001 ||
                !roleRows.Any(row => BarValue(row, 2) == 0) || !roleRows.Any(row => BarValue(row, 2) == 100))
                throw new Exception("Role bars: played share, win rate or boundary values incorrect");
            Console.WriteLine("OK WPF : barres des rôles, proportions de parties et victoires, bornes 0 et 100 %.");
            var output = Environment.GetEnvironmentVariable("RIFT_UI_PREVIEW_DIR");
            foreach (var width in new[] { 1050, 1220, 1920 })
            {
                view.Width = width; view.Height = 1000;
                view.Measure(new Size(width, 1000)); view.Arrange(new Rect(0, 0, width, 1000)); view.UpdateLayout();
                if (Control<ScrollViewer>("ProfileScroll").ExtentWidth > Control<ScrollViewer>("ProfileScroll").ViewportWidth + 1)
                    throw new Exception("Profile horizontally overflows at " + width);
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 1000, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"profile-{width}.png")); png.Save(file);
                }
            }
            Console.WriteLine("OK WPF : disposition compacte contrôlée à 1050, 1220 et 1920 px (données synthétiques).");
        }
        finally { await view.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        Console.WriteLine("OK WPF : arrêt de la détection personnelle sans attente du prochain cycle.");
    }
}
