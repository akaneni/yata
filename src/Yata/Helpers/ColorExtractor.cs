using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yata.Helpers;

/// <summary>Picks a vivid accent color from album art that reads well on black.</summary>
internal static class ColorExtractor
{
    public static readonly Color Default = Color.FromRgb(0x8A, 0xB4, 0xFF);

    public static Color FromImage(BitmapSource source)
    {
        const int size = 12;
        var scaled = new TransformedBitmap(source,
            new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

        int width = bgra.PixelWidth, height = bgra.PixelHeight;
        var pixels = new byte[width * height * 4];
        bgra.CopyPixels(pixels, width * 4, 0);

        // Weight saturated, mid-lightness pixels so gray backgrounds don't wash out the color.
        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double pb = pixels[i] / 255.0, pg = pixels[i + 1] / 255.0, pr = pixels[i + 2] / 255.0;
            var (_, s, l) = ToHsl(pr, pg, pb);
            double weight = 0.02 + s * (1 - Math.Abs(l - 0.5) * 2);
            r += pr * weight;
            g += pg * weight;
            b += pb * weight;
            total += weight;
        }

        if (total <= 0)
            return Default;

        var (hue, saturation, lightness) = ToHsl(r / total, g / total, b / total);
        if (saturation < 0.08)
            return Default;

        saturation = Math.Max(saturation, 0.45);
        lightness = Math.Clamp(lightness, 0.62, 0.74);
        return FromHsl(hue, saturation, lightness);
    }

    private static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d < 1e-6)
            return (0, 0, l);

        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
                 : max == g ? (b - r) / d + 2
                 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        return Color.FromRgb(Channel(p, q, h + 1.0 / 3), Channel(p, q, h), Channel(p, q, h - 1.0 / 3));
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        double v = t < 1.0 / 6 ? p + (q - p) * 6 * t
                 : t < 0.5 ? q
                 : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
                 : p;
        return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    }
}
