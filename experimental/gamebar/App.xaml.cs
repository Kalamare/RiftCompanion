using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Rift.GameBarProbe;

sealed partial class App : Application
{
    // Game Bar requires this object to survive until the widget window closes.
    private XboxGameBarWidget? widget;

    public App()
    {
        InitializeComponent();
        Suspending += (_, _) => widget = null;
    }

    protected override void OnActivated(IActivatedEventArgs args)
    {
        if (args is not XboxGameBarWidgetActivatedEventArgs activation || !activation.IsLaunchActivation)
            return;

        var frame = new Frame();
        Window.Current.Content = frame;
        widget = new XboxGameBarWidget(activation, Window.Current.CoreWindow, frame);
        frame.Navigate(typeof(ProbePage), true);
        Window.Current.Closed += (_, _) => widget = null;
        Window.Current.Activate();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (args.PrelaunchActivated) return;
        var frame = new Frame();
        Window.Current.Content = frame;
        frame.Navigate(typeof(ProbePage), false);
        Window.Current.Activate();
    }
}
