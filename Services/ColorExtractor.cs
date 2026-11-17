using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services;

/// <summary>Extracts a vivid, readable-on-black accent colour from album art.</summary>
internal static class ColorExtractor
{
    private static readonly Color Neutral = Color.FromRgb(0xE5, 0xE5, 0xEA);

    public static Color Vibrant(BitmapSource source)
    {
        return ExtractPalette(source).Primary;
    }

    /// <summary>
    /// Extracts both a primary and secondary vibrant hue from album art for dual-stop ambient shimmer.
    /// </summary>
    public static (Color Primary, Color Secondary) ExtractPalette(BitmapSource source)
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
            if (sat < 0.12) return (Neutral, Color.FromRgb(0x8E, 0x8E, 0x93)); // essentially monochrome artwork

            var primary = FromHsv(hue, Math.Clamp(sat * 1.15, 0.45, 0.85), 0.95);

            // Accumulate a second cluster of pixels whose hue differs from primary hue by >= 30°
            double r2 = 0, g2 = 0, b2 = 0, total2 = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                double pb = pixels[i] / 255.0, pg = pixels[i + 1] / 255.0, pr = pixels[i + 2] / 255.0;
                ToHsv(pr, pg, pb, out double ph, out double s, out double v);
                double diff = Math.Abs(ph - hue);
                if (diff > 180) diff = 360 - diff;
                if (diff >= 28 && s > 0.15)
                {
                    double w2 = s * v * (diff / 180.0);
                    r2 += pr * w2; g2 += pg * w2; b2 += pb * w2; total2 += w2;
                }
            }

            Color secondary;
            if (total2 > 0.05)
            {
                ToHsv(r2 / total2, g2 / total2, b2 / total2, out double hue2, out double sat2, out _);
                secondary = FromHsv(hue2, Math.Clamp(sat2 * 1.1, 0.40, 0.85), 0.88);
            }
            else
            {
                // Analogous shifted hue when cover art has a single dominant color family
                double shiftedHue = (hue + 38.0) % 360.0;
                secondary = FromHsv(shiftedHue, Math.Clamp(sat * 1.05, 0.40, 0.80), 0.85);
            }

            return (primary, secondary);
        }
        catch
        {
            return (Neutral, Color.FromRgb(0x8E, 0x8E, 0x93));
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

    /// <summary>
    /// Applies extended Reinhard-filmic tone mapping to an sRGB color to prevent harsh clipping,
    /// channel blowout, and banding when rendering ambient glowing halos on HDR and wide-gamut displays.
    /// </summary>
    public static Color ToneMapHdr(Color color, double exposure = 1.0, double maxLuminance = 2.2)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        // Linearize sRGB to linear photometric space
        double lr = r <= 0.04045 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        double lg = g <= 0.04045 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        double lb = b <= 0.04045 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        // Apply exposure scaling
        lr *= exposure;
        lg *= exposure;
        lb *= exposure;

        // Extended Reinhard tone mapping: mapped = (val * (1 + val / (maxLum^2))) / (1 + val)
        double maxLumSq = maxLuminance * maxLuminance;
        double mr = (lr * (1.0 + lr / maxLumSq)) / (1.0 + lr);
        double mg = (lg * (1.0 + lg / maxLumSq)) / (1.0 + lg);
        double mb = (lb * (1.0 + lb / maxLumSq)) / (1.0 + lb);

        // Compress back to gamma sRGB space
        byte fr = (byte)Math.Clamp(Math.Round((mr <= 0.0031308 ? 12.92 * mr : 1.055 * Math.Pow(mr, 1.0 / 2.4) - 0.055) * 255.0), 0, 255);
        byte fg = (byte)Math.Clamp(Math.Round((mg <= 0.0031308 ? 12.92 * mg : 1.055 * Math.Pow(mg, 1.0 / 2.4) - 0.055) * 255.0), 0, 255);
        byte fb = (byte)Math.Clamp(Math.Round((mb <= 0.0031308 ? 12.92 * mb : 1.055 * Math.Pow(mb, 1.0 / 2.4) - 0.055) * 255.0), 0, 255);

        return Color.FromArgb(color.A, fr, fg, fb);
    }
}
