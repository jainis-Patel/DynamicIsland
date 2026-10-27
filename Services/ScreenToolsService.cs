using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Foundation.Metadata;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Provides screen pixel color eyedropper sampling (<c>#RRGGBB</c>) and built-in Windows 10/11
/// <see cref="OcrEngine"/> optical character recognition for clipboard screenshots and images.
/// </summary>
internal static class ScreenToolsService
{
    /// <summary>
    /// Samples the physical screen pixel color at the current cursor position.
    /// </summary>
    public static (bool Success, Color Color, string Hex, string RgbText) SampleCursorPixelColor()
    {
        if (!GetCursorPos(out var pt))
        {
            return (false, Colors.Black, "#000000", "rgb(0, 0, 0)");
        }

        return SamplePixelColorAt(pt.X, pt.Y);
    }

    public static (bool Success, Color Color, string Hex, string RgbText) SamplePixelColorAt(int x, int y)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return (false, Colors.Black, "#000000", "rgb(0, 0, 0)");
        }

        try
        {
            uint pixel = GetPixel(hdc, x, y);
            if (pixel == 0xFFFFFFFF)
            {
                return (false, Colors.Black, "#000000", "rgb(0, 0, 0)");
            }

            // COLORREF is 0x00BBGGRR
            byte r = (byte)(pixel & 0xFF);
            byte g = (byte)((pixel >> 8) & 0xFF);
            byte b = (byte)((pixel >> 16) & 0xFF);
            return FormatColorSample(Color.FromRgb(r, g, b));
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    public static (bool Success, Color Color, string Hex, string RgbText) FormatColorSample(Color color)
    {
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        string rgb = $"rgb({color.R}, {color.G}, {color.B})";
        return (true, color, hex, rgb);
    }

    /// <summary>
    /// Runs Windows 10/11 local <see cref="OcrEngine"/> on a WPF <see cref="BitmapSource"/> and returns recognized text.
    /// </summary>
    public static async Task<string?> ExtractTextFromBitmapAsync(BitmapSource? bitmap)
    {
        if (bitmap == null) return null;

        try
        {
            if (!ApiInformation.IsTypePresent("Windows.Media.Ocr.OcrEngine"))
            {
                return null;
            }

            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return null;

            using var mem = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            encoder.Save(mem);
            mem.Position = 0;

            var randomAccessStream = mem.AsRandomAccessStream();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var result = await engine.RecognizeAsync(softwareBitmap);
            string? text = result?.Text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenToolsService] OCR failed: {ex.Message}");
            return null;
        }
    }
}
