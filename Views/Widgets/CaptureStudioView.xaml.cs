using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class CaptureStudioView : UserControl
{
    private IslandWindow? _window;
    private Color _currentColor = Color.FromRgb(10, 132, 255);

    public CaptureStudioView()
    {
        InitializeComponent();
        Loaded += CaptureStudioView_Loaded;
    }

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        (_window ?? IslandWindow.Instance)?.NavigateToView(Core.ExpandedView.Launcher);

    private void CaptureStudioView_Loaded(object sender, RoutedEventArgs e)
    {
        _window = IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;
        UpdatePinnedCountBadge();
    }

    public void UpdatePinnedCountBadge()
    {
        int count = Application.Current.Windows.OfType<PinnedCaptureWindow>().Count();
        UnpinAllText.Text = count > 0 ? $"Unpin {count} Floating Snip(s)" : "Unpin Floating Snips";
    }

    private void NewSnipBtn_Click(object sender, RoutedEventArgs e)
    {
        _window?.LaunchElementCaptureOverlay();
    }

    private void EyedropperBtn_Click(object sender, RoutedEventArgs e)
    {
        var sample = ScreenToolsService.SampleCursorPixelColor();
        if (sample.Success)
        {
            _currentColor = sample.Color;
            ColorSwatch.Background = new SolidColorBrush(_currentColor);
            HexCodeText.Text = sample.Hex;
            RgbCodeText.Text = sample.RgbText;
            _window?.TriggerLiquidImpulse(20, -7, 2);
            _window?.ShowToast(
                "\uEF3C",
                "Color Sampled",
                $"{sample.Hex} ({sample.RgbText})",
                sample.Color,
                "capturestudio");
        }
    }

    private void CopyHexBtn_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(HexCodeText.Text);
        _window?.ShowToast(
            "\uE8C8",
            "Hex Copied",
            HexCodeText.Text,
            _currentColor,
            "capturestudio");
        _window?.TriggerLiquidImpulse(18, -6, 2);
    }

    private void UnpinAllSnips_Click(object sender, RoutedEventArgs e)
    {
        var pinned = Application.Current.Windows.OfType<PinnedCaptureWindow>().ToList();
        foreach (var win in pinned)
        {
            try { win.Close(); } catch { }
        }

        UpdatePinnedCountBadge();
        _window?.ShowToast(
            "\uE77A",
            "Screen Snips Unpinned",
            $"Closed {pinned.Count} floating capture window(s)",
            ThemeBrushes.BlueColor,
            "capturestudio");
        _window?.TriggerLiquidImpulse(22, -8, 3);
    }

    private async void PasteImageOcr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                var bmp = Clipboard.GetImage();
                if (bmp != null)
                {
                    OcrStatusText.Text = "Running Windows OCR…";
                    OcrStatusText.Foreground = ThemeBrushes.Accent;
                    string? text = await ScreenToolsService.ExtractTextFromBitmapAsync(bmp);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        OcrResultBox.Text = text;
                        int words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        OcrStatusText.Text = $"Extracted {words} words";
                        OcrStatusText.Foreground = ThemeBrushes.Green;
                        _window?.TriggerLiquidImpulse(24, -8, 3);
                    }
                    else
                    {
                        OcrStatusText.Text = "No text recognized in image";
                        OcrStatusText.Foreground = ThemeBrushes.Amber;
                    }
                }
            }
            else if (Clipboard.ContainsText())
            {
                OcrResultBox.Text = Clipboard.GetText();
                OcrStatusText.Text = "Pasted clipboard text";
                OcrStatusText.Foreground = ThemeBrushes.Accent;
            }
            else
            {
                OcrStatusText.Text = "No image in clipboard";
                OcrStatusText.Foreground = ThemeBrushes.Amber;
            }
        }
        catch (Exception ex)
        {
            OcrStatusText.Text = "OCR error: " + ex.Message;
            OcrStatusText.Foreground = ThemeBrushes.Red;
        }
    }

    private void OcrDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OcrDropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            e.Handled = true;
            string file = files[0];
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
            {
                try
                {
                    OcrStatusText.Text = $"Extracting from {Path.GetFileName(file)}…";
                    OcrStatusText.Foreground = ThemeBrushes.Accent;
                    string? text = await ScreenToolsService.ExtractTextFromFileAsync(file);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        OcrResultBox.Text = text;
                        int words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        OcrStatusText.Text = $"Extracted {words} words from {Path.GetFileName(file)}";
                        OcrStatusText.Foreground = ThemeBrushes.Green;
                        _window?.TriggerLiquidImpulse(28, -10, 4);
                    }
                    else
                    {
                        OcrStatusText.Text = "No text detected in image";
                        OcrStatusText.Foreground = ThemeBrushes.Amber;
                    }
                }
                catch (Exception ex)
                {
                    OcrStatusText.Text = "OCR error: " + ex.Message;
                    OcrStatusText.Foreground = ThemeBrushes.Red;
                }
            }
            else
            {
                OcrStatusText.Text = "Please drop an image file (PNG/JPG/BMP/WebP)";
                OcrStatusText.Foreground = ThemeBrushes.Amber;
            }
        }
    }

    private void CopyOcrText_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(OcrResultBox.Text))
        {
            try
            {
                Clipboard.SetText(OcrResultBox.Text);
                _window?.ShowToast(
                    "\uE8C8",
                    "OCR Text Copied",
                    $"{OcrResultBox.Text.Length} characters copied to clipboard",
                    ThemeBrushes.GreenColor,
                    "capturestudio");
                _window?.TriggerLiquidImpulse(18, -6, 2);
            }
            catch (Exception ex)
            {
                App.Log($"[CaptureStudioView] Clipboard copy failed: {ex.Message}");
            }
        }
    }

    private void ClearOcrText_Click(object sender, RoutedEventArgs e)
    {
        OcrResultBox.Clear();
        OcrStatusText.Text = "Cleared";
        OcrStatusText.Foreground = ThemeBrushes.SecondaryText;
    }
}
