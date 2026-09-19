using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rift.Desktop;

// Official bundled assets decoded off the UI thread, with native vector fallbacks.
internal static class ProfileIcons
{
    private static readonly IReadOnlyDictionary<string, string> OfficialFiles = new Dictionary<string, string>
    {
        ["cs"] = "minion.png", ["gold"] = "items.png", ["sword"] = "score.png",
        ["team"] = "champion.png", ["vision"] = "item-3340.png", ["ward"] = "item-3340.png", ["controlward"] = "item-2055.png",
        ["top"] = "role-Top.png", ["jungle"] = "role-Jungle.png", ["middle"] = "role-Mid.png",
        ["bottom"] = "role-Bot.png", ["utility"] = "role-Support.png"
    };
    private static IReadOnlyDictionary<string, ImageSource?> official = new Dictionary<string, ImageSource?>();
    public static async Task PrepareAsync(ProfileBitmapCache cache, CancellationToken token)
    {
        var loaded = await Task.Run(() => OfficialFiles.ToDictionary(pair => pair.Key,
            pair => (ImageSource?)cache.Get(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Ui", pair.Value), trimTransparent: true)), token);
        token.ThrowIfCancellationRequested();
        official = loaded;
    }
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["gold"] = "M 12,3 A 9,9 0 1 1 11.99,3 M 15,7 L 10,7 8,10 15,14 13,17 8,17 M 12,5 L 12,19",
        ["sword"] = "M 5,20 L 10,15 M 6,12 L 13,19 M 9,14 L 18,3 21,3 21,6 11,16",
        ["vision"] = "M 2,12 Q 12,0 22,12 Q 12,24 2,12 M 12,8 A 4,4 0 1 1 11.99,8",
        ["cs"] = "M 6,20 L 6,11 9,5 15,5 18,11 18,20 Z M 6,12 L 18,12 M 10,16 L 10,18 M 14,16 L 14,18",
        ["win"] = "M 7,3 L 17,3 17,9 Q 17,15 12,15 Q 7,15 7,9 Z M 7,5 L 3,5 3,9 7,11 M 17,5 L 21,5 21,9 17,11 M 12,15 L 12,21 M 7,21 L 17,21",
        ["assist"] = "M 9,3 L 15,3 15,9 21,9 21,15 15,15 15,21 9,21 9,15 3,15 3,9 9,9 Z",
        ["death"] = "M 6,15 Q 1,5 9,3 Q 21,0 21,10 Q 21,14 18,15 L 18,21 7,21 Z M 8,9 L 8,12 M 16,9 L 16,12 M 11,17 L 11,21 M 14,17 L 14,21",
        ["team"] = "M 8,3 A 3,3 0 1 1 7.99,3 M 17,6 A 3,3 0 1 1 16.99,6 M 2,21 L 2,17 Q 8,10 14,17 L 14,21 M 15,15 Q 22,14 22,21",
        ["ward"] = "M 12,3 L 18,9 12,15 6,9 Z M 12,15 L 12,21 M 8,21 L 16,21 M 10,9 L 14,9",
        ["top"] = "M 3,21 L 3,3 21,3 16,8 8,8 8,16 Z M 13,13 L 21,13 21,21 13,21 Z",
        ["jungle"] = "M 12,22 Q 1,17 3,6 L 9,13 7,2 Q 15,7 12,17 L 20,7 Q 22,17 12,22",
        ["middle"] = "M 3,3 L 10,3 3,10 Z M 21,14 L 21,21 14,21 Z M 3,18 L 18,3 21,6 6,21 Z",
        ["bottom"] = "M 3,21 L 21,21 21,3 16,8 16,16 8,16 Z M 3,3 L 11,3 11,11 3,11 Z",
        ["utility"] = "M 12,3 L 16,8 12,13 8,8 Z M 8,11 L 2,8 5,15 10,17 M 16,11 L 22,8 19,15 14,17 M 12,13 L 12,22",
        ["rank"] = "M 12,2 L 21,6 19,16 12,22 5,16 3,6 Z M 12,6 L 17,11 12,17 7,11 Z M 3,9 L 0,6 M 21,9 L 24,6"
    };
    public static Image Create(string key, string label, double size = 22, string color = "#D6BE83")
    {
        if (official.TryGetValue(key, out var bitmap) && bitmap is not null)
            return new Image { Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform, ToolTip = label,
                Margin = new Thickness(0,0,9,0), VerticalAlignment = VerticalAlignment.Center };
        var geometry = Geometry.Parse(Paths.GetValueOrDefault(key, Paths["team"])); geometry.Freeze();
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; brush.Freeze();
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0,0,24,24))));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(brush, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry));
        drawing.Freeze(); var source = new DrawingImage(drawing); source.Freeze();
        return new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform, ToolTip = label, Margin = new Thickness(0,0,9,0), VerticalAlignment = VerticalAlignment.Center };
    }
    public static string RankColor(string? tier) => tier switch
    {
        "IRON" => "#A5A1A0", "BRONZE" => "#C08C63", "SILVER" => "#CBD8DF", "GOLD" => "#EAC86C",
        "PLATINUM" => "#73D2CF", "EMERALD" => "#69D8A1", "DIAMOND" => "#9DBEFF",
        "MASTER" => "#CE91EA", "GRANDMASTER" => "#ED8585", "CHALLENGER" => "#F5D38B", _ => "#8095A4"
    };
}
