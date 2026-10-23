using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rift.Core;

namespace Rift.Desktop;

public partial class ProfileView
{
    private void OnQuickMode(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string tag } && int.TryParse(tag, out var mode)) SelectCommonMode(mode); }
    private void OnMoreMode(object sender, SelectionChangedEventArgs e)
    { if (MoreModes.SelectedValue is int mode && mode >= 0) SelectCommonMode(mode); }
    private void SelectCommonMode(int mode)
    {
        if (QueueBox.ItemsSource is null && profile is not null) UpdateQueueFilters();
        var ready = statsReady; statsReady = false;
        foreach (var box in new[] { SummaryMode, ChampionsMode, RolesMode }) box.SelectedValue = 0;
        statsReady = ready;
        QueueBox.SelectedValue = mode;
        if (profile is not null && !isPreparing) Render();
    }
    private void RenderCommonModes()
    {
        int selected = QueueBox.SelectedValue is int q ? q : 0;
        foreach (var button in QuickModes.Children.OfType<Button>())
        {
            bool active = button.Tag?.ToString() == selected.ToString();
            button.Background = (Brush)new BrushConverter().ConvertFromString(active ? "#29423D" : "#262626")!;
            button.Foreground = active ? Brushes.Turquoise : Brushes.LightSteelBlue;
        }
        var modes = new Dictionary<int, string> { [-2] = "Plus de modes…", [1700] = "Arène", [400] = "Normal · Draft", [430] = "Normal · Aveugle", [490] = "Partie rapide" };
        foreach (var match in profile?.Matches ?? [])
            if (match.Queue is not (0 or 420 or 440 or 450)) modes[match.Queue] = QueueCatalog.Label(match.Queue);
        MoreModes.SelectionChanged -= OnMoreMode;
        MoreModes.ItemsSource = modes; MoreModes.SelectedValue = modes.ContainsKey(selected) ? selected : -2;
        MoreModes.SelectionChanged += OnMoreMode;
    }
}
