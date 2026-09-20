using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class ControlThemeChecks
{
    public static async Task Run()
    {
        var content = new StackPanel { Margin = new Thickness(28) };
        var board = new Border { Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)), Child = content, Width = 680 };
        TextBlock Label(string text) => new() { Text = text, Foreground = Brushes.White, FontSize = 16, Margin = new Thickness(0, 12, 0, 12) };
        content.Children.Add(Label("Rift Companion · contrôles"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var button = new Button { Content = "Actualiser", Margin = new Thickness(0, 0, 8, 0) };
        var toggle = new ToggleButton { Content = "Débug", IsChecked = true, Margin = new Thickness(0, 0, 8, 0) };
        actions.Children.Add(button); actions.Children.Add(toggle);
        actions.Children.Add(new Button { Content = "Indisponible", IsEnabled = false });
        content.Children.Add(actions);
        content.Children.Add(Label("Mode de jeu"));
        var combo = new ComboBox { DisplayMemberPath = "Value", SelectedValuePath = "Key", HorizontalAlignment = HorizontalAlignment.Left, Width = 270 };
        combo.ItemsSource = new[] { new KeyValuePair<string, string>("all", "Tous les modes"), new("solo", "Classée Solo / Duo"), new("flex", "Classée Flex") };
        combo.SelectedIndex = 1; content.Children.Add(combo);
        var expander = new Expander { Header = "Plus de statistiques", Content = Label("Statistiques du profil"), Foreground = Brushes.LightGray, Margin = new Thickness(0, 14, 0, 0) };
        content.Children.Add(expander);
        var rows = new StackPanel();
        for (var i = 0; i < 30; i++) rows.Children.Add(Label($"Partie {i + 1:00} · victoire"));
        var scroll = new ScrollViewer { Content = rows, Height = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        content.Children.Add(scroll);
        var horizontal = new ScrollBar { Orientation = Orientation.Horizontal, Maximum = 100, ViewportSize = 20, Value = 35, Margin = new Thickness(0, 16, 0, 0) };
        content.Children.Add(horizontal);
        // WPF coerces IsDropDownOpen to false before Loaded; host only the synthetic gallery.
        var host = new Window { Content = board, Width = 700, Height = 580, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
        host.Show();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
        Layout(board);

        // Check the template contracts that allow selection, expansion and scrolling to work.
        if (button.Template.FindName("Surface", button) is not Border) throw new Exception("Application button theme not loaded");
        var dropToggle = (ToggleButton)combo.Template.FindName("DropDownToggle", combo);
        dropToggle.IsChecked = true;
        if (!combo.IsDropDownOpen) throw new Exception("ComboBox open binding failed");
        combo.SelectedValue = "flex";
        if (combo.SelectedIndex != 2) throw new Exception("ComboBox value selection failed");
        var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
        var output = Environment.GetEnvironmentVariable("RIFT_UI_PREVIEW_DIR");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            Save((FrameworkElement)popup.Child, Path.Combine(output, "controls-menu.png"), 270, 160);
        }
        combo.IsDropDownOpen = false;
        if (dropToggle.IsChecked != false) throw new Exception("ComboBox close binding failed");
        expander.IsExpanded = true; Layout(board);
        if (((ContentPresenter)expander.Template.FindName("ExpandedContent", expander)).Visibility != Visibility.Visible)
            throw new Exception("Expander content no longer opens");
        expander.IsExpanded = false;
        scroll.ScrollToVerticalOffset(90); Layout(board);
        if (scroll.VerticalOffset <= 0) throw new Exception("Themed scroll viewer cannot scroll");
        var vertical = Descendants(scroll).OfType<ScrollBar>().First(x => x.Orientation == Orientation.Vertical);
        foreach (var bar in new[] { vertical, horizontal })
        {
            var track = (Track)bar.Template.FindName("PART_Track", bar);
            if (track.Orientation != bar.Orientation || Math.Abs(track.Value - bar.Value) > 0.01 || track.Thumb.ActualHeight <= 0 || track.Thumb.ActualWidth <= 0)
                throw new Exception("Scrollbar track contract failed");
            var before = bar.Value;
            (bar.Orientation == Orientation.Vertical ? ScrollBar.PageDownCommand : ScrollBar.PageRightCommand).Execute(null, bar);
            Layout(board);
            if (bar.Value <= before) throw new Exception("Scrollbar page command failed");
        }
        if (output is not null) Save(board, Path.Combine(output, "controls.png"), 680, 540);
        Console.WriteLine("OK WPF : thème commun, sélection et ouverture/fermeture des menus, sections repliables et défilement vertical/horizontal.");
        }
        finally { combo.IsDropDownOpen = false; host.Close(); }
    }

    static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(680, 540)); element.Arrange(new Rect(0, 0, 680, 540)); element.UpdateLayout();
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
    static void Save(FrameworkElement element, string path, int width, int height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
