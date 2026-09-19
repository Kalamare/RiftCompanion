using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rift.Desktop;

static class Program
{
    [STAThread]
    static int Main()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        int exit = 0;
        dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await Run(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); exit = 1; }
            finally { dispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run(); return exit;
    }
    static async Task Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "RiftUiChecks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var vault = new ApiKeyVault(folder);
            vault.Save("test-only-secret");
            if (new ApiKeyVault(folder).Read() != "test-only-secret" || System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder,"riot-key.dpapi"))).Contains("test-only-secret")) throw new Exception("DPAPI roundtrip/ciphertext failed");
            vault.Forget(); if (vault.Read() is not null) throw new Exception("DPAPI forget failed");
            Console.WriteLine("OK Windows : clé de test chiffrée par DPAPI, restaurée puis supprimée ; aucun stockage en clair.");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(128,128,96,96,PixelFormats.Bgra32,null,new byte[128*128*4],128*4)));
            using (var file = File.Create(Path.Combine(folder,"icon.png"))) encoder.Save(file);
            var bytes = File.ReadAllBytes(Path.Combine(folder,"icon.png"));
            for (int i = 0; i < 160; i++) File.WriteAllBytes(Path.Combine(folder,$"{i}.png"),bytes);
            int ticks = 0, duringDecode = 0; double maximum = 0; var watch = Stopwatch.StartNew(); var last = watch.Elapsed.TotalMilliseconds;
            bool decoding = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { ticks++; if (decoding) duringDecode++; var now = watch.Elapsed.TotalMilliseconds; maximum = Math.Max(maximum, now-last); last = now; };
            timer.Start();
            var cache = new ProfileBitmapCache(); var uiThread = Environment.CurrentManagedThreadId;
            decoding = true;
            var icons = await Task.Run(() =>
            {
                if (Environment.CurrentManagedThreadId == uiThread) throw new Exception("Decode on UI thread");
                return Enumerable.Range(0,160).Select(i => cache.Get(Path.Combine(folder,$"{i}.png"))!).ToArray();
            });
            decoding = false;
            await Task.Delay(60); timer.Stop();
            if (icons.Any(i => i is null || !i.IsFrozen || i.PixelWidth != 48)) throw new Exception("Frozen thumbnail contract failed");
            var same = await Task.Run(() => cache.Get(Path.Combine(folder,"0.png")));
            if (!ReferenceEquals(same,icons[0])) throw new Exception("Bitmap decoded twice");
            var visual = new ProfileVisual(62,"MonkeyKing",true); var changes = new List<string?>();
            visual.PropertyChanged += (_,e) => changes.Add(e.PropertyName);
            visual.Update("Wukong",icons[0]); visual.Update("Wukong",icons[0]);
            if (changes.Count != 1 || changes[0] != "Icon") throw new Exception("Redundant visual notifications");
            var empty = new ProfileVisual(0, "", false);
            if (!empty.IsEmpty || empty.Fallback != "" || visual.IsEmpty) throw new Exception("Empty item placeholder regression");
            foreach (var symbol in new[] { "gold", "sword", "vision", "cs", "win", "assist", "death", "team", "ward", "top", "jungle", "middle", "bottom", "utility", "rank" })
            {
                var icon = ProfileIcons.Create(symbol, symbol);
                if (icon.Source is not DrawingImage drawing || !drawing.IsFrozen || drawing.Width <= 0) throw new Exception("Invalid vector icon: " + symbol);
            }
            Console.WriteLine("OK WPF : emplacement vide sans glyphe ; 15 pictogrammes vectoriels valides et gelés.");
            await ProfileIcons.PrepareAsync(cache, CancellationToken.None);
            foreach (var symbol in new[] { "cs", "gold", "sword", "team", "vision", "ward", "controlward", "top", "jungle", "middle", "bottom", "utility" })
            {
                if (ProfileIcons.Create(symbol, symbol).Source is not BitmapSource bitmap || !bitmap.IsFrozen || bitmap.PixelWidth is <= 0 or > 48)
                    throw new Exception("Official icon missing or not prepared: " + symbol);
            }
            var minion = ProfileIcons.Create("cs", "CS").Source;
            await ProfileIcons.PrepareAsync(cache, CancellationToken.None);
            if (!ReferenceEquals(minion, ProfileIcons.Create("cs", "CS").Source)) throw new Exception("Official icons decoded twice");
            Console.WriteLine("OK WPF : 12 usages d’icônes officielles, bitmaps gelés et cache réutilisé.");
            var padded = new byte[48 * 48 * 4];
            for (int y = 16; y < 32; y++) for (int x = 20; x < 28; x++) padded[(y * 48 + x) * 4 + 3] = 255;
            var paddedEncoder = new PngBitmapEncoder();
            paddedEncoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(48,48,96,96,PixelFormats.Bgra32,null,padded,48*4)));
            var paddedPath = Path.Combine(folder, "padded.png");
            using (var file = File.Create(paddedPath)) paddedEncoder.Save(file);
            var trimmed = await Task.Run(() => cache.Get(paddedPath, trimTransparent: true));
            var original = await Task.Run(() => cache.Get(paddedPath));
            if (trimmed is null || !trimmed.IsFrozen || trimmed.PixelWidth != 8 || trimmed.PixelHeight != 16 || original?.PixelWidth != 48)
                throw new Exception("Transparent bounds normalization or cache separation failed");
            Console.WriteLine("OK WPF : cadrage transparent normalisé, proportions conservées, image originale indépendante.");
            if (ticks == 0) throw new Exception("Dispatcher stalled");
            Console.WriteLine($"OK WPF : 160 PNG décodés hors UI, gelés à 48 px, réutilisés et notifications minimales. Ticks pendant décodage : {duringDecode}, écart maximal observé : {maximum:F0} ms. Test synthétique, pas une mesure en jeu.");
            await PersonalProfileUiChecks.Run(folder);
        }
        finally { Directory.Delete(folder,true); }
    }
}
