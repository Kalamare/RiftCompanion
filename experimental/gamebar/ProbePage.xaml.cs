using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Rift.GameBarProbe;

public sealed partial class ProbePage : Page
{
    private int clicks;
    public ProbePage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        Mode.Text = args.Parameter is true
            ? "Affiché par Windows Game Bar."
            : "Fenêtre de présentation uniquement. Ouvre Win+G puis le widget Rift Companion — test plein écran.";
    }
    private void OnClick(object sender, RoutedEventArgs args) => Result.Text = $"Clics reçus : {++clicks}.";
}
