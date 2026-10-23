using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Rift.Core;

namespace Rift.Desktop;

// Static vector charts: no animation, timer, web view or additional data request.
public static class ProfileCharts
{
    public static readonly Brush Win = new SolidColorBrush(Color.FromRgb(69, 145, 255));
    public static readonly Brush Loss = new SolidColorBrush(Color.FromRgb(246, 105, 119));
    private static readonly Brush Track = new SolidColorBrush(Color.FromRgb(53, 63, 77));
    public static Brush RankColor(string tier) => (Brush)new BrushConverter().ConvertFromString(tier switch
    {
        "IRON" => "#9A8E85", "BRONZE" => "#B67B50", "SILVER" => "#A8BBC8", "GOLD" => "#E7BB58",
        "PLATINUM" => "#56BDB3", "EMERALD" => "#38CC8C", "DIAMOND" => "#35AFEA",
        "MASTER" => "#BA65DB", "GRANDMASTER" => "#ED6268", "CHALLENGER" => "#ECCD81", _ => "#9FB3C1"
    })!;
    private static TextBlock Label(string text, double size = 12) => new() { Text = text, FontSize = size,
        Foreground = Brushes.LightSteelBlue, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    public static Grid Donut(int wins, int games)
    {
        wins = Math.Clamp(wins, 0, Math.Max(0, games));
        var grid = new Grid { Width = 90, Height = 90, Margin = new Thickness(0, 0, 18, 0),
            ToolTip = games > 0 ? $"{wins} victoires · {games - wins} défaites · {100.0 * wins / games:F1} %" : "Aucune partie pour ce filtre" };
        grid.Children.Add(new Ellipse { Width = 86, Height = 86, Stroke = games > 0 ? Loss : Track, StrokeThickness = 10 });
        if (wins == games && games > 0)
            grid.Children.Add(new Ellipse { Width = 86, Height = 86, Stroke = Win, StrokeThickness = 10 });
        else if (wins > 0)
        {
            double angle = 2 * Math.PI * wins / games;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(45, 7), false, false);
                context.ArcTo(new Point(45 + 38 * Math.Sin(angle), 45 - 38 * Math.Cos(angle)), new Size(38, 38), 0, wins * 2 > games, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            grid.Children.Add(new Path { Data = geometry, Stroke = Win, StrokeThickness = 10 });
        }
        var text = Label(games > 0 ? $"{100.0 * wins / games:F0}%" : "—", 19);
        text.VerticalAlignment = VerticalAlignment.Center; text.Foreground = Win; text.FontWeight = FontWeights.SemiBold;
        grid.Children.Add(text);
        System.Windows.Automation.AutomationProperties.SetName(grid, grid.ToolTip.ToString());
        return grid;
    }

    public static FrameworkElement RoleBars(IEnumerable<(string Role, int Games, int Wins)> source)
    {
        var rows = source.ToArray();
        string[] order = ["top", "jungle", "middle", "bottom", "utility"];
        int max = Math.Max(1, rows.Select(r => Math.Max(r.Wins, r.Games - r.Wins)).DefaultIfEmpty(0).Max());
        var panel = new StackPanel();
        var chart = new UniformGrid { Columns = 5, Margin = new Thickness(0, 8, 0, 4) };
        foreach (var role in order)
        {
            var data = rows.Where(r => r.Role == role).ToArray();
            int games = data.Sum(r => r.Games), wins = data.Sum(r => r.Wins);
            var label = Roles.Labels.GetValueOrDefault(role, role);
            var column = new StackPanel { ToolTip = $"{label} : {games} parties · {wins} V / {games - wins} D" + (games > 0 ? $" · {100.0 * wins / games:F1} %" : "") };
            var bars = new StackPanel { Orientation = Orientation.Horizontal, Height = 84, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            bars.Children.Add(new Border { Width = 9, Height = 80.0 * wins / max, Background = Win, CornerRadius = new CornerRadius(3, 3, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 0) });
            bars.Children.Add(new Border { Width = 9, Height = 80.0 * (games - wins) / max, Background = Loss, CornerRadius = new CornerRadius(3, 3, 0, 0), VerticalAlignment = VerticalAlignment.Bottom });
            column.Children.Add(new Viewbox { Child = bars, Height = 58, Stretch = Stretch.Uniform });
            var icon = ProfileIcons.Create(role, label, 22); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.Margin = new Thickness(0, 7, 0, 3);
            column.Children.Add(icon);
            System.Windows.Automation.AutomationProperties.SetName(column, column.ToolTip.ToString());
            chart.Children.Add(column);
        }
        panel.Children.Add(chart);
        panel.Children.Add(Label(rows.Sum(r => r.Games) == 0 ? "Aucune partie avec rôle standard" : "Bleu : victoires · Rouge : défaites", 10));
        int unknown = rows.Where(r => !order.Contains(r.Role)).Sum(r => r.Games);
        if (unknown > 0) panel.Children.Add(Label($"{unknown} parties sans rôle renseigné, hors graphique", 10));
        return panel;
    }

    public static FrameworkElement RankRoad(RankEntry rank)
    {
        string[] tiers = ["IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER"];
        string[] codes = ["I", "B", "S", "G", "P", "E", "D", "M", "GM", "C"];
        int tier = Array.IndexOf(tiers, rank.Tier);
        int division = rank.Division switch { "IV" => 0, "III" => 1, "II" => 2, "I" => 3, _ => -1 };
        if (tier < 0 || (tier < 7 && division < 0)) return Label("Progression indisponible");
        string[] stops = tier < 7 ? [codes[tier] + "4", codes[tier] + "3", codes[tier] + "2", codes[tier] + "1", codes[tier + 1] + (tier == 6 ? "" : "4")] : ["M", "GM", "C"];
        int current = tier < 7 ? division : tier - 7;
        var result = new StackPanel { Margin = new Thickness(0, 16, 0, 0), ToolTip = "Position actuelle dans les divisions, pas un historique. Les promotions dépendent des règles du classement." };
        var road = new UniformGrid { Columns = stops.Length };
        for (int i = 0; i < stops.Length; i++)
        {
            var step = new StackPanel();
            var color = RankColor(tiers[tier < 7 ? (i == 4 ? tier + 1 : tier) : 7 + i]);
            var marker = new Grid { Height = 30 };
            marker.Children.Add(new Border { Height = 3, Background = Track, VerticalAlignment = VerticalAlignment.Center });
            marker.Children.Add(new Ellipse { Width = 17, Height = 17,
                Stroke = color, StrokeThickness = 2, Fill = i == current ? color : Brushes.White });
            if (i == current)
            {
                marker.Children.Add(new TextBlock { Text = "✓", Foreground = Brushes.White, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                marker.Children.Add(new Polygon { Points = new PointCollection([new Point(0,0), new Point(8,0), new Point(4,5)]), Fill = color, Width = 8, Height = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top });
            }
            step.Children.Add(marker); var text = Label(stops[i], 11); text.Foreground = color; if (i == current) text.FontWeight = FontWeights.Bold;
            step.Children.Add(text); road.Children.Add(step);
        }
        result.Children.Add(road);
        result.ToolTip += tier < 7 ? $"\n{rank.Lp} LP · {Math.Max(0, 100 - rank.Lp)} LP jusqu’au prochain seuil." : $"\n{rank.Lp} LP · seuils GM / Challenger variables.";
        return result;
    }
}
