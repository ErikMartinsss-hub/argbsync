using System.Windows.Media;
using SdkColor = OpenRGB.NET.Color;

namespace ArgbSync.App.Helpers;

public static class ColorConversion
{
    public static Color ToMedia(SdkColor c) => Color.FromRgb(c.R, c.G, c.B);

    public static SdkColor ToSdk(Color c) => new(c.R, c.G, c.B);

    public static SdkColor Scale(SdkColor c, double factor)
    {
        factor = Math.Clamp(factor, 0.0, 1.0);
        static byte ScaleByte(byte v, double f) => (byte)Math.Round(v * f);
        return new SdkColor(ScaleByte(c.R, factor), ScaleByte(c.G, factor), ScaleByte(c.B, factor));
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryFromHex(string? hex, out Color color)
    {
        color = Colors.White;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }
}