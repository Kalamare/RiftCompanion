using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Rift.Desktop;

public partial class OverlayBanner : Window
{
    private readonly DispatcherTimer expiry = new() { Interval = TimeSpan.FromSeconds(12) };
    public event Action? OpenRequested;
    public OverlayBanner()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, -20, new nint(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000L));
            HwndSource.FromHwnd(hwnd)?.AddHook(NoActivate);
        };
        expiry.Tick += (_, _) => Hide();
        IsVisibleChanged += (_, _) => { if (IsVisible) expiry.Start(); else expiry.Stop(); };
        Closed += (_, _) => expiry.Stop();
    }
    private void OnOpen(object sender, RoutedEventArgs e) { Hide(); OpenRequested?.Invoke(); }
    private void OnDismiss(object sender, RoutedEventArgs e) => Hide();
    private static nint NoActivate(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE: deliver click without stealing game focus.
        return 0;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
