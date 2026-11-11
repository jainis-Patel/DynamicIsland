using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Foundation.Metadata;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Provides screen pixel color eyedropper sampling (<c>#RRGGBB</c>), physical screen region capture,
/// and built-in Windows 10/11 <see cref="OcrEngine"/> optical character recognition for screenshots and shelf images.
/// </summary>
internal static class ScreenToolsService
{
    /// <summary>
    /// Captures a physical screen rectangle using GDI <c>BitBlt</c> and returns a detached, frozen <see cref="BitmapSource"/>.
    /// </summary>
    public static BitmapSource? CaptureScreenRegion(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;

        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldObj = IntPtr.Zero;

        try
        {
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return null;

            hBitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (hBitmap == IntPtr.Zero) return null;

            oldObj = SelectObject(memDc, hBitmap);
            if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SRCCOPY))
            {
                return null;
            }

            var bmp = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenToolsService] CaptureScreenRegion failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (oldObj != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldObj);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Runs Windows 10/11 local <see cref="OcrEngine"/> on an image file on disk.
    /// </summary>
    public static async Task<string?> ExtractTextFromFileAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                fs,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            var frame = decoder.Frames[0];
            frame.Freeze();
            return await ExtractTextFromBitmapAsync(frame);
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenToolsService] ExtractTextFromFileAsync failed: {ex.Message}");
            return null;
        }
    }
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
