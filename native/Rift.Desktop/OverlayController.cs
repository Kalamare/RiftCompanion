using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Rift.Core;
using Rift.Infrastructure;

namespace Rift.Desktop;

// A normal opaque HWND above a borderless game: no injection, render hook or frame timer.
internal sealed class OverlayController
{
    private readonly Window owner;
    private readonly string directory;
    private readonly Func<string, string, CancellationToken, Task<PlayerProfile>> loader;
    private readonly PersonalProfileDetector detector;
    private readonly Action<string> report;
    private readonly Func<string, string, CancellationToken, Task<OpggProfile?>> opggLoader;
    private readonly HwndSource source;
    private readonly OverlayKeyboard keyboard;
    private readonly WinEventProc foregroundCallback;
    private readonly nint eventHook;
    private readonly DispatcherTimer focusCheck = new() { Interval = TimeSpan.FromSeconds(1) };
    private nint lastForeground;
    private string lastShortcut = "Aucun raccourci reçu depuis le démarrage.";
    private bool shortcutContext;
    private OverlayWindow? view;
    private OverlayBanner? banner;
    private nint announcedGame;
    private nint game;
    private bool detached, disposed;
    private bool hasPlacement, placedDetached;
    private nint placedGame;
    public OverlayController(Window owner, string directory, Func<string, string, CancellationToken, Task<PlayerProfile>> loader,
        Func<CancellationToken, Task<Credentials?>> discover, Action<string> report, Func<string, string, CancellationToken, Task<OpggProfile?>> opggLoader)
    {
        this.owner = owner; this.directory = directory; this.loader = loader; this.report = report; this.opggLoader = opggLoader;
        detector = new(new LcuTransport(), discover);
        source = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle)!; source.AddHook(WndProc);
        keyboard = new(source.Handle);
        // Read the current foreground when dispatched: queued events can already be stale.
        foregroundCallback = (_, _, _, _, _, _, _) => { if (!disposed) owner.Dispatcher.BeginInvoke(CheckForeground); };
        eventHook = SetWinEventHook(3, 3, 0, foregroundCallback, 0, 0, 0);
        // Cheap recovery if Windows misses a focus event or device registration fails.
        focusCheck.Tick += (_, _) => CheckForeground();
        focusCheck.Start(); CheckForeground();
    }
    private OverlayWindow View
    {
        get
        {
            if (view is null) { view = new(directory, loader, token => detector.DetectAsync(token), opggLoader: opggLoader); view.DetachRequested += ToggleDetach; }
            return view;
        }
    }
    private static bool IsGame(nint hwnd)
    {
        if (hwnd == 0) return false;
        var windowClass = new StringBuilder(128);
        GetClassName(hwnd, windowClass, windowClass.Capacity);
        if (windowClass.ToString() == "RiotWindowClass") return true;
        GetWindowThreadProcessId(hwnd, out var pid);
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    private void CheckForeground()
    {
        if (disposed) return;
        var current = GetForegroundWindow();
        if (current == lastForeground && keyboard.Enabled == shortcutContext) return;
        lastForeground = current;
        ForegroundChanged(current);
    }
    private void ForegroundChanged(nint hwnd)
    {
        if (disposed) return;
        bool inGame = IsGame(hwnd); if (inGame) game = hwnd;
        bool inOverlay = view is { IsVisible: true } && hwnd == new WindowInteropHelper(view).Handle;
        shortcutContext = inGame || inOverlay;
        if (shortcutContext)
        {
            keyboard.SetEnabled(true);
            report(keyboard.Enabled ? $"LoL/overlay détecté · clavier Raw Input actif · {lastShortcut}" : $"Clavier Raw Input : erreur Windows {keyboard.Error}. Réessai automatique.");
            if (inGame && keyboard.Enabled && announcedGame != game)
            {
                announcedGame = game;
                if (view is not { IsVisible: true }) ShowBanner();
            }
        }
        else
        {
            keyboard.SetEnabled(false);
            banner?.Hide();
            if (!detached && view is { IsVisible: true }) view.Hide();
            report($"Clavier overlay en veille hors partie/overlay · {lastShortcut}");
        }
    }
    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x00FF && !disposed) // WM_INPUT; leave cleanup to DefWindowProc.
        {
            CheckForeground();
            if (!shortcutContext) return 0;
            int action = keyboard.Read(lParam);
            if (action != 0)
            {
                lastShortcut = $"{(action == 1 ? "Ctrl+X" : "Ctrl+Maj+X")} reçu à {DateTime.Now:HH:mm:ss}.";
                report($"Clavier Raw Input · {lastShortcut}");
                using var operation = RuntimeDiagnostics.Begin("Overlay", action == 1 ? "Raccourci afficher/masquer reçu" : "Raccourci détacher reçu");
                if (action == 1) Toggle(); else ToggleDetach();
            }
        }
        return 0;
    }
    public void Toggle()
    {
        banner?.Hide();
        if (View.IsVisible) { View.Hide(); return; }
        View.UseLive(); ShowPlaced();
    }
    public void ToggleDetach() { banner?.Hide(); detached = !detached; ShowPlaced(); }
    private void ShowBanner()
    {
        if (!GetWindowRect(game, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return;
        if (banner is null)
        {
            banner = new OverlayBanner();
            banner.OpenRequested += () => { detached = false; View.UseLive(); ShowPlaced(); };
        }
        var monitor = MonitorFromWindow(game, 2);
        uint dpi = 96;
        try { if (GetDpiForMonitor(monitor, 0, out var x, out _) == 0) dpi = x; } catch (DllNotFoundException) { }
        int width = Math.Min((int)(340 * dpi / 96.0), rect.Right - rect.Left);
        int height = (int)(76 * dpi / 96.0);
        int margin = (int)(16 * dpi / 96.0);
        var hwnd = new WindowInteropHelper(banner).EnsureHandle();
        // Position before showing, to avoid a flash on the primary display.
        SetWindowPos(hwnd, new nint(-1), Math.Max(rect.Left, rect.Right - width - margin), rect.Top + margin, width, height, 0x0010);
        banner.Show();
    }
    public async Task Example()
    {
        detached = true; SetStyle(); await View.ShowExample(); if (!disposed) Place();
    }
    private void ShowPlaced()
    {
        if (!IsGame(game))
        {
            game = 0;
            foreach (var process in Process.GetProcessesByName("League of Legends")) using (process)
                if (process.MainWindowHandle != 0) { game = process.MainWindowHandle; break; }
        }
        if (game == 0) detached = true;
        SetStyle(); View.WindowState = WindowState.Normal; View.Show();
        if (!hasPlacement || placedDetached != detached || placedGame != game) Place();
    }
    private void SetStyle()
    {
        View.Topmost = !detached;
        // Per-pixel transparency requires a borderless WPF window in both modes.
        View.WindowStyle = WindowStyle.None;
        View.ResizeMode = detached ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
        View.ShowInTaskbar = detached;
        var hwnd = new WindowInteropHelper(View).EnsureHandle();
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        SetWindowLongPtr(hwnd, -20, new nint(detached ? style & ~0x08000000L : style | 0x08000000L)); // WS_EX_NOACTIVATE
    }
    private void Place()
    {
        var monitors = new List<nint>();
        MonitorEnum callback = (nint monitor, nint _, ref NativeRect rect, nint __) => { monitors.Add(monitor); return true; };
        EnumDisplayMonitors(0, 0, callback, 0);
        var current = MonitorFromWindow(game != 0 ? game : new WindowInteropHelper(owner).Handle, 2);
        var target = detached ? monitors.FirstOrDefault(m => m != current) : current;
        if (target == 0) { target = current; if (detached) report("Un seul écran détecté : overlay détaché sur cet écran."); }
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() }; if (!GetMonitorInfo(target, ref info)) return;
        var area = info.Work;
        if (!detached && game != 0 && GetWindowRect(game, out var gameRect) && gameRect.Right > gameRect.Left && gameRect.Bottom > gameRect.Top) area = gameRect;
        uint dpi = 96; try { if (GetDpiForMonitor(target, 0, out var x, out _) == 0) dpi = x; } catch (DllNotFoundException) { }
        var width = Math.Min((int)(1280 * dpi / 96.0), area.Right - area.Left - 24);
        var height = Math.Min((int)(830 * dpi / 96.0), area.Bottom - area.Top - 24);
        SetWindowPos(new WindowInteropHelper(View).Handle, detached ? new nint(-2) : new nint(-1), area.Left + (area.Right - area.Left - width) / 2,
            area.Top + (area.Bottom - area.Top - height) / 2, Math.Max(320, width), Math.Max(240, height), 0x0010 | 0x0020);
        hasPlacement = true; placedDetached = detached; placedGame = game;
    }
    public async Task StopAsync()
    {
        disposed = true; focusCheck.Stop(); keyboard.Dispose(); if (eventHook != 0) UnhookWinEvent(eventHook); source.RemoveHook(WndProc);
        banner?.Close();
        if (view is not null) await view.StopAsync(); detector.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    private delegate void WinEventProc(nint hook, uint evt, nint hwnd, int objectId, int childId, uint thread, uint time);
    private delegate bool MonitorEnum(nint monitor, nint hdc, ref NativeRect rect, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnum callback, nint data);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int kind, out uint x, out uint y);
}
