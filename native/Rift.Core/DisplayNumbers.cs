using System.Globalization;
namespace Rift.Core;
public static class DisplayNumbers
{
    private static readonly NumberFormatInfo Format = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NumberGroupSizes = [3] };
    public static string Exact(double number) => number.ToString("N0", Format);
    public static string Compact(double number)
    {
        var abs = Math.Abs(number);
        if (abs >= 999_950) return (number / 1_000_000).ToString("0.#", Format) + " M";
        if (abs >= 1000) return (number / 1000).ToString("0.#", Format) + " k";
        return Exact(number);
    }
}
