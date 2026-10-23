using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rift.Core;
using Rift.Desktop;

static class ProfileChartChecks
{
    public static void Run()
    {
        var empty = ProfileCharts.Donut(0, 0);
        if (((TextBlock)empty.Children[^1]).Text != "—") throw new Exception("Empty donut must not show 0% winrate");
        foreach (int wins in new[] { 0, 1, 2 })
        {
            var donut = ProfileCharts.Donut(wins, 2);
            if (((TextBlock)donut.Children[^1]).Text != $"{50 * wins}%") throw new Exception("Donut percentage mismatch");
        }
        var roles = (StackPanel)ProfileCharts.RoleBars([("top", 10, 8), ("utility", 4, 0)]);
        var bars = (UniformGrid)roles.Children[0];
        var top = (StackPanel)((Viewbox)((StackPanel)bars.Children[0]).Children[0]).Child;
        var support = (StackPanel)((Viewbox)((StackPanel)bars.Children[4]).Children[0]).Child;
        if (((Border)top.Children[0]).Height != 80 || ((Border)top.Children[1]).Height != 20 ||
            ((Border)support.Children[0]).Height != 0 || ((Border)support.Children[1]).Height != 40)
            throw new Exception("Role chart must share a count scale and include utility/support");
        var rank = new RankEntry("RANKED_SOLO_5x5", "DIAMOND", "III", 44, 203, 205);
        var road = (StackPanel)ProfileCharts.RankRoad(rank);
        var steps = (UniformGrid)road.Children[0];
        if (steps.Children.Count != 5 || ((TextBlock)((StackPanel)steps.Children[1]).Children[1]).Text != "D3" ||
            ((TextBlock)((StackPanel)steps.Children[4]).Children[1]).Text != "M") throw new Exception("Diamond road endpoints");
        var diamondMarker = (Grid)((StackPanel)steps.Children[1]).Children[0];
        var masterMarker = (Grid)((StackPanel)steps.Children[4]).Children[0];
        if (((System.Windows.Shapes.Ellipse)diamondMarker.Children[1]).Stroke.ToString() == ((System.Windows.Shapes.Ellipse)masterMarker.Children[1]).Stroke.ToString() ||
            !diamondMarker.Children.OfType<TextBlock>().Any(t => t.Text == "✓")) throw new Exception("Rank road must distinguish Master and mark current division");
        foreach (string tier in new[] { "MASTER", "GRANDMASTER", "CHALLENGER" })
            if (((UniformGrid)((StackPanel)ProfileCharts.RankRoad(rank with { Tier = tier })).Children[0]).Children.Count != 3)
                throw new Exception("Apex road must not invent divisions");
        if (Environment.GetEnvironmentVariable("RIFT_UI_PREVIEW_DIR") is { } output)
        {
            Directory.CreateDirectory(output);
            var content = new StackPanel { Margin = new Thickness(20) };
            content.Children.Add(ProfileCharts.Donut(7, 10)); content.Children.Add(roles); content.Children.Add(road);
            var frame = new Border { Background = new SolidColorBrush(Color.FromRgb(34, 39, 47)), Child = content, Width = 350, Height = 390 };
            frame.Measure(new Size(350, 390)); frame.Arrange(new Rect(0, 0, 350, 390)); frame.UpdateLayout();
            var bitmap = new RenderTargetBitmap(350, 390, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, "charts.png")); png.Save(stream);
        }
        Console.WriteLine("OK graphiques : anneau vide/0/50/100 %, barres victoires/défaites avec échelle commune, Support, route Diamant et apex.");
    }
}
