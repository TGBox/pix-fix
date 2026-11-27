namespace PixFix.Core.Common;

/// <summary>
/// Pure WCAG 2.1 contrast and color adaptation helpers so dark imported document colors stay legible in Dark Mode
/// without mutating Light Mode or exported colors.
/// </summary>
public static class ThemeColorContrast
{
    public readonly record struct RgbColor(byte R, byte G, byte B);

    public static readonly RgbColor DarkEditorSurface = new(37, 40, 48);

    public const double MinimumDarkTextContrast = 5.5;
    public const double TargetDarkTextContrast = 6.5;

    public static bool IsNearWhiteBackground(RgbColor color) =>
        color.R >= 245 && color.G >= 245 && color.B >= 245;

    public static bool IsDefaultDarkText(RgbColor color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        return (max - min) <= 24 && max <= 75;
    }

    public static double ContrastRatio(RgbColor a, RgbColor b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static RgbColor AdaptForDarkSurface(RgbColor color)
    {
        if (ContrastRatio(color, DarkEditorSurface) >= MinimumDarkTextContrast)
        {
            return color;
        }

        var (h, s, l) = RgbToHsl(color);
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        if ((max - min) <= 24)
        {
            s = 0;
        }
        else
        {
            s = Math.Min(s, 0.88);
        }

        var low = Math.Max(l, 0.50);
        var high = 0.94;
        for (var i = 0; i < 16; i++)
        {
            var mid = (low + high) * 0.5;
            var candidate = HslToRgb(h, s, mid);
            if (ContrastRatio(candidate, DarkEditorSurface) >= TargetDarkTextContrast)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return HslToRgb(h, s, high);
    }

    private static double RelativeLuminance(RgbColor color) =>
        (0.2126 * Linearize(color.R))
        + (0.7152 * Linearize(color.G))
        + (0.0722 * Linearize(color.B));

    private static double Linearize(byte channel)
    {
        var srgb = channel / 255.0;
        return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    private static (double H, double S, double L) RgbToHsl(RgbColor color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) * 0.5;
        var d = max - min;

        if (d <= 1e-9)
        {
            return (0, 0, l);
        }

        var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (Math.Abs(max - r) < 1e-9)
        {
            h = ((g - b) / d) + (g < b ? 6.0 : 0.0);
        }
        else if (Math.Abs(max - g) < 1e-9)
        {
            h = ((b - r) / d) + 2.0;
        }
        else
        {
            h = ((r - g) / d) + 4.0;
        }

        return (h / 6.0, s, l);
    }

    private static RgbColor HslToRgb(double h, double s, double l)
    {
        if (s <= 1e-9)
        {
            var gray = ToByte(l);
            return new(gray, gray, gray);
        }

        var q = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
        var p = (2.0 * l) - q;
        return new(
            ToByte(HueToRgb(p, q, h + (1.0 / 3.0))),
            ToByte(HueToRgb(p, q, h)),
            ToByte(HueToRgb(p, q, h - (1.0 / 3.0))));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0.0)
        {
            t += 1.0;
        }
        else if (t > 1.0)
        {
            t -= 1.0;
        }

        if (t < 1.0 / 6.0)
        {
            return p + ((q - p) * 6.0 * t);
        }

        if (t < 0.5)
        {
            return q;
        }

        if (t < 2.0 / 3.0)
        {
            return p + ((q - p) * ((2.0 / 3.0) - t) * 6.0);
        }

        return p;
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
