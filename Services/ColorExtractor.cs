using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services;

/// <summary>Extracts a vivid, readable-on-black accent colour from album art.</summary>
internal static class ColorExtractor
{
    private static readonly Color Neutral = Color.FromRgb(0xE5, 0xE5, 0xEA);

    public static Color Vibrant(BitmapSource source)
    {
        try
        {
            var small = new TransformedBitmap(source,
                new ScaleTransform(16.0 / source.PixelWidth, 16.0 / source.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);

            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);

            // Weight each pixel by saturation² × brightness so greys/blacks don't wash out the hue.
            double r = 0, g = 0, b = 0, total = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                double pb = pixels[i] / 255.0, pg = pixels[i + 1] / 255.0, pr = pixels[i + 2] / 255.0;
                ToHsv(pr, pg, pb, out _, out double s, out double v);
                double weight = s * s * v + 0.001;
                r += pr * weight; g += pg * weight; b += pb * weight; total += weight;
            }

            ToHsv(r / total, g / total, b / total, out double hue, out double sat, out _);
            if (sat < 0.12) return Neutral; // essentially monochrome artwork

            // Normalise for a dark UI: punchy saturation, high brightness.
            return FromHsv(hue, Math.Clamp(sat * 1.15, 0.45, 0.85), 0.95);
        }
        catch
        {
            return Neutral;
        }
    }

    private static void ToHsv(double r, double g, double b, out double h, out double s, out double v)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        v = max;
        s = max <= 0 ? 0 : delta / max;
        if (delta <= 0) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / delta) % 6);
        else if (max == g) h = 60 * (((b - r) / delta) + 2);
        else h = 60 * (((r - g) / delta) + 4);
        if (h < 0) h += 360;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
