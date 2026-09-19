using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rift.Desktop;

// Called from a background worker only; frozen bitmaps can safely be handed to WPF's dispatcher.
public sealed class ProfileBitmapCache
{
    private readonly Dictionary<string, BitmapSource?> cache = [];
    public BitmapSource? Get(string? path, int width = 48, bool trimTransparent = false)
    {
        if (path is null) return null;
        var key = $"{path}|{width}|{trimTransparent}";
        if (cache.TryGetValue(key, out var image)) return image;
        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = width; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            BitmapSource result = trimTransparent ? TrimTransparent(bitmap) : bitmap;
            if (cache.Count >= 512) cache.Clear();
            cache[key] = result; return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { cache[key] = null; return null; }
    }
    // Normalize the visible footprint of Riot UI assets, without altering the source PNG.
    private static BitmapSource TrimTransparent(BitmapSource bitmap)
    {
        var pixels = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int width = pixels.PixelWidth, height = pixels.PixelHeight, stride = width * 4;
        var bytes = new byte[stride * height]; pixels.CopyPixels(bytes, stride, 0);
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (bytes[y * stride + x * 4 + 3] > 8)
                { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right < left) return bitmap;
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        cropped.Freeze(); return cropped;
    }
}
