using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.LiquidGlass;

namespace DynamicIsland.Views;

public partial class GlassPlayground : Window
{
    private readonly GlassRenderer _renderer;
    private readonly StaticBackdropProvider _backdrop;
    private readonly Stopwatch _frameStopwatch = new();

    private Point _mainCenter = new(480, 260);
    private Size _mainSize = new(380, 160);

    private Point _dropletCenter = new(720, 260);
    private Size _dropletSize = new(90, 90);

    private bool _isDraggingMain;
    private bool _isDraggingDroplet;
    private Point _lastDragMouse;

    private int _renderWidth = 1280;
    private int _renderHeight = 720;
    private bool _isInitialized;

    public GlassPlayground()
    {
        InitializeComponent();

        _renderer = new GlassRenderer();
        _backdrop = new StaticBackdropProvider(_renderer.Device, _renderer.Context);

        // Populate Presets
        CmbPreset.Items.Add("Regular (macOS Balanced)");
        CmbPreset.Items.Add("Clear (Crystal High-IOR)");
        CmbPreset.Items.Add("Dark (Obsidian OLED)");
        CmbPreset.Items.Add("Tinted (Apple Accent)");
        CmbPreset.Items.Add("Capsule Button (Prominent Rim)");
        CmbPreset.Items.Add("Dynamic Island Pill");
        CmbPreset.Items.Add("Expanded Card Surface");
        CmbPreset.SelectedIndex = 0;

        _backdrop.GenerateTestPattern(_renderWidth, _renderHeight);
        _isInitialized = true;

        Loaded += (_, _) => RenderFrame();
    }

    private void ApplyPreset(GlassSettings preset)
    {
        _isInitialized = false;

        SldThickness.Value = preset.Thickness;
        SldBevelWidth.Value = preset.BevelWidth;
        SldIor.Value = preset.IndexOfRefraction;
        SldBlurRadius.Value = preset.BlurRadius;
        SldSaturation.Value = preset.Saturation;
        SldBrightness.Value = preset.Brightness;
        SldTintOpacity.Value = preset.TintColor.A / 255.0;
        SldSpecular.Value = preset.SpecularStrength;
        SldRimPower.Value = preset.RimPower;
        SldDispersion.Value = preset.Dispersion;
        SldCornerRadius.Value = preset.CornerRadius;
        SldCornerSmoothing.Value = preset.CornerSmoothing;
        SldMergeSmoothness.Value = preset.MergeSmoothness;

        CmbShape.SelectedIndex = (int)preset.Shape;

        _isInitialized = true;
        UpdateLabelTexts();
        RenderFrame();
    }

    private void UpdateLabelTexts()
    {
        LblThickness.Text = SldThickness.Value.ToString("F1");
        LblBevelWidth.Text = SldBevelWidth.Value.ToString("F1");
        LblIor.Text = SldIor.Value.ToString("F2");
        LblBlurRadius.Text = SldBlurRadius.Value.ToString("F1");
        LblSaturation.Text = SldSaturation.Value.ToString("F2");
        LblBrightness.Text = SldBrightness.Value.ToString("F2");
        LblTintOpacity.Text = SldTintOpacity.Value.ToString("F2");
        LblSpecular.Text = SldSpecular.Value.ToString("F2");
        LblRimPower.Text = SldRimPower.Value.ToString("F2");
        LblDispersion.Text = SldDispersion.Value.ToString("F2");
        LblCornerRadius.Text = SldCornerRadius.Value.ToString("F0");
        LblCornerSmoothing.Text = SldCornerSmoothing.Value.ToString("F2");
        LblMergeSmoothness.Text = SldMergeSmoothness.Value.ToString("F1");
    }

    private GlassSettings CollectCurrentSettings()
    {
        byte tintA = (byte)Math.Clamp((int)(SldTintOpacity.Value * 255.0), 0, 255);
        Color tint = Color.FromArgb(tintA, 0x10, 0x12, 0x1C);

        return GlassSettings.Create()
            .WithThickness((float)SldThickness.Value)
            .WithBevelWidth((float)SldBevelWidth.Value)
            .WithIndexOfRefraction((float)SldIor.Value)
            .WithBlurRadius((float)SldBlurRadius.Value)
            .WithSaturation((float)SldSaturation.Value)
            .WithBrightness((float)SldBrightness.Value)
            .WithTint(tint)
            .WithSpecular((float)SldSpecular.Value, (float)SldRimPower.Value)
            .WithDispersion((float)SldDispersion.Value)
            .WithCornerRadius((float)SldCornerRadius.Value)
            .WithCornerSmoothing((float)SldCornerSmoothing.Value)
            .WithShape((GlassShapeType)Math.Clamp(CmbShape.SelectedIndex, 0, 3))
            .WithMergeSmoothness((float)SldMergeSmoothness.Value)
            .Build();
    }

    private void RenderFrame()
    {
        if (!_isInitialized || _backdrop.CurrentFrameSRV == null) return;

        UpdateLabelTexts();
        var settings = CollectCurrentSettings();

        // Build shape array
        bool includeSecondary = ChkSecondaryDroplet.IsChecked ?? false;
        int shapeCount = includeSecondary ? 2 : 1;
        var shapes = new GlassShape[shapeCount];

        shapes[0] = GlassShape.FromBounds(
            (float)(_mainCenter.X - _mainSize.Width * 0.5),
            (float)(_mainCenter.Y - _mainSize.Height * 0.5),
            (float)_mainSize.Width,
            (float)_mainSize.Height,
            settings.CornerRadius,
            settings.Shape);

        if (includeSecondary)
        {
            shapes[1] = GlassShape.FromBounds(
                (float)(_dropletCenter.X - _dropletSize.Width * 0.5),
                (float)(_dropletCenter.Y - _dropletSize.Height * 0.5),
                (float)_dropletSize.Width,
                (float)_dropletSize.Height,
                (float)(_dropletSize.Width * 0.5),
                GlassShapeType.Circle);
        }

        _frameStopwatch.Restart();
        var bitmap = _renderer.RenderToBitmap(
            _renderWidth,
            _renderHeight,
            1.0f,
            settings,
            shapes,
            _backdrop.CurrentFrameSRV);
        _frameStopwatch.Stop();

        RenderOutputImage.Source = bitmap;

        double ms = _frameStopwatch.Elapsed.TotalMilliseconds;
        double fps = ms > 0.001 ? Math.Min(1000.0 / ms, 240.0) : 240.0;
        TxtMetrics.Text = $"GPU Frame: {ms:F2} ms · ~{fps:F0} FPS · {_renderWidth}x{_renderHeight} · {shapeCount} Glass Bodies";
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) RenderFrame();
    }

    private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;

        GlassSettings preset = CmbPreset.SelectedIndex switch
        {
            1 => GlassPresets.Clear,
            2 => GlassPresets.Dark,
            3 => GlassPresets.Tinted(Color.FromRgb(0x0A, 0x84, 0xFF), 0.40f),
            4 => GlassPresets.CapsuleButton,
            5 => GlassPresets.Pill,
            6 => GlassPresets.Card,
            _ => GlassPresets.Regular
        };

        ApplyPreset(preset);
    }

    private void CmbBackdrop_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;

        if (CmbBackdrop.SelectedIndex == 1)
        {
            GenerateSunsetWallpaper();
        }
        else
        {
            _backdrop.GenerateTestPattern(_renderWidth, _renderHeight);
        }
        RenderFrame();
    }

    private void GenerateSunsetWallpaper()
    {
        int width = _renderWidth;
        int height = _renderHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int y = 0; y < height; y++)
        {
            float t = (float)y / height;
            byte r = (byte)(30 + 200 * t);
            byte g = (byte)(20 + 80 * t);
            byte b = (byte)(50 + 40 * (1.0f - t));

            for (int x = 0; x < width; x++)
            {
                int idx = y * stride + x * 4;
                pixels[idx + 0] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 255;
            }
        }

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        _backdrop.LoadBitmap(bmp);
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingMain = true;
        _lastDragMouse = e.GetPosition(ViewportGrid);
        ViewportGrid.CaptureMouse();
    }

    private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingDroplet = true;
        _lastDragMouse = e.GetPosition(ViewportGrid);
        ViewportGrid.CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingMain && !_isDraggingDroplet) return;

        Point current = e.GetPosition(ViewportGrid);
        Vector delta = current - _lastDragMouse;
        _lastDragMouse = current;

        // Scale by viewport vs render resolution
        double scaleX = _renderWidth / Math.Max(ViewportGrid.ActualWidth, 1.0);
        double scaleY = _renderHeight / Math.Max(ViewportGrid.ActualHeight, 1.0);

        if (_isDraggingMain)
        {
            _mainCenter.X = Math.Clamp(_mainCenter.X + delta.X * scaleX, 100, _renderWidth - 100);
            _mainCenter.Y = Math.Clamp(_mainCenter.Y + delta.Y * scaleY, 60, _renderHeight - 60);
        }
        else if (_isDraggingDroplet)
        {
            _dropletCenter.X = Math.Clamp(_dropletCenter.X + delta.X * scaleX, 50, _renderWidth - 50);
            _dropletCenter.Y = Math.Clamp(_dropletCenter.Y + delta.Y * scaleY, 50, _renderHeight - 50);
        }

        RenderFrame();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingMain = false;
        ViewportGrid.ReleaseMouseCapture();
    }

    private void Viewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingDroplet = false;
        ViewportGrid.ReleaseMouseCapture();
    }

    private void BtnGoldenFrame_Click(object sender, RoutedEventArgs e)
    {
        if (RenderOutputImage.Source is not BitmapSource bitmap) return;

        string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "liquid-glass");
        Directory.CreateDirectory(outDir);

        string outPath = Path.Combine(outDir, "golden_output.png");
        using var stream = new FileStream(outPath, FileMode.Create);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);

        MessageBox.Show($"Golden image captured and saved successfully to:\n{outPath}", "Golden Frame Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        CmbPreset.SelectedIndex = 0;
        _mainCenter = new Point(480, 260);
        _dropletCenter = new Point(720, 260);
        ApplyPreset(GlassPresets.Regular);
    }

    protected override void OnClosed(EventArgs e)
    {
        _backdrop.Dispose();
        _renderer.Dispose();
        base.OnClosed(e);
    }
}
