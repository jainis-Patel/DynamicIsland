using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Views;

public partial class CaptureOverlayWindow : Window
{
    private enum AnnotationTool { None, Box, Arrow, Redact }

    private readonly DropShelfService _shelfService;
    private BitmapSource? _screenBitmap;
    private double _dpiScale = 1.0;
    private Point _selectStart;
    private Rect _selectedDipRect;
    private bool _isSelecting;
    private bool _hasSelection;
    private AnnotationTool _activeTool = AnnotationTool.None;
    private Point _drawStart;
    private Shape? _activeShape;

    internal CaptureOverlayWindow(DropShelfService shelfService)
    {
        _shelfService = shelfService;
        InitializeComponent();
        PrepareMonitorBoundsAndSnapshot();
    }

    private void PrepareMonitorBoundsAndSnapshot()
    {
        if (!GetCursorPos(out var pt))
        {
            pt = new POINT { X = 0, Y = 0 };
        }

        IntPtr mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
        {
            if (GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
            {
                _dpiScale = dpiX / 96.0;
            }

            _screenBitmap = ScreenToolsService.CaptureScreenRegion(
                mi.rcMonitor.Left,
                mi.rcMonitor.Top,
                mi.rcMonitor.Width,
                mi.rcMonitor.Height);

            Left = mi.rcMonitor.Left / _dpiScale;
            Top = mi.rcMonitor.Top / _dpiScale;
            Width = mi.rcMonitor.Width / _dpiScale;
            Height = mi.rcMonitor.Height / _dpiScale;

            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, IntPtr.Zero, mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Width, mi.rcMonitor.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }
        else
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            _screenBitmap = ScreenToolsService.CaptureScreenRegion(0, 0, (int)Width, (int)Height);
        }

        if (_screenBitmap != null)
        {
            BackgroundScreenshot.Source = _screenBitmap;
        }
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ToolbarBorder.IsMouseOver) return;

        Point pos = e.GetPosition(OverlayCanvas);
        if (!_hasSelection || _activeTool == AnnotationTool.None)
        {
            _isSelecting = true;
            _hasSelection = false;
            _selectStart = pos;
            AnnotationCanvas.Children.Clear();
            SelectionBorder.Visibility = Visibility.Visible;
            ToolbarBorder.Visibility = Visibility.Collapsed;
            UpdateSelectionBox(pos, pos);
        }
        else
        {
            _drawStart = pos;
            if (_activeTool == AnnotationTool.Box)
            {
                _activeShape = new Rectangle
                {
                    Stroke = ThemeBrushes.Amber,
                    StrokeThickness = 2.5,
                    RadiusX = 4,
                    RadiusY = 4
                };
                AnnotationCanvas.Children.Add(_activeShape);
            }
            else if (_activeTool == AnnotationTool.Redact)
            {
                _activeShape = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1A)),
                    Stroke = ThemeBrushes.BorderSubtle,
                    StrokeThickness = 1
                };
                AnnotationCanvas.Children.Add(_activeShape);
            }
            else if (_activeTool == AnnotationTool.Arrow)
            {
                _activeShape = new Line
                {
                    X1 = pos.X,
                    Y1 = pos.Y,
                    X2 = pos.X,
                    Y2 = pos.Y,
                    Stroke = ThemeBrushes.Red,
                    StrokeThickness = 3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Triangle
                };
                AnnotationCanvas.Children.Add(_activeShape);
            }
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        Point cur = e.GetPosition(OverlayCanvas);

        if (_isSelecting)
        {
            UpdateSelectionBox(_selectStart, cur);
        }
        else if (_activeShape != null)
        {
            if (_activeShape is Rectangle rect)
            {
                double x = Math.Min(_drawStart.X, cur.X);
                double y = Math.Min(_drawStart.Y, cur.Y);
                rect.Width = Math.Max(2, Math.Abs(cur.X - _drawStart.X));
                rect.Height = Math.Max(2, Math.Abs(cur.Y - _drawStart.Y));
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
            }
            else if (_activeShape is Line line)
            {
                line.X2 = cur.X;
                line.Y2 = cur.Y;
            }
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting)
        {
            _isSelecting = false;
            if (_selectedDipRect.Width > 12 && _selectedDipRect.Height > 12)
            {
                _hasSelection = true;
                ToolbarBorder.Visibility = Visibility.Visible;
            }
        }
        _activeShape = null;
    }

    private void UpdateSelectionBox(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(b.X - a.X);
        double h = Math.Abs(b.Y - a.Y);
        _selectedDipRect = new Rect(x, y, w, h);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = w;
        SelectionBorder.Height = h;
    }

    private BitmapSource? RenderCroppedCapture()
    {
        if (_screenBitmap == null || _selectedDipRect.Width < 4 || _selectedDipRect.Height < 4)
            return null;

        try
        {
            ToolbarBorder.Visibility = Visibility.Collapsed;
            SelectionBorder.Visibility = Visibility.Collapsed;
            OverlayCanvas.UpdateLayout();

            int pxX = Math.Max(0, (int)Math.Round(_selectedDipRect.X * _dpiScale));
            int pxY = Math.Max(0, (int)Math.Round(_selectedDipRect.Y * _dpiScale));
            int pxW = Math.Min(_screenBitmap.PixelWidth - pxX, (int)Math.Round(_selectedDipRect.Width * _dpiScale));
            int pxH = Math.Min(_screenBitmap.PixelHeight - pxY, (int)Math.Round(_selectedDipRect.Height * _dpiScale));
            if (pxW <= 0 || pxH <= 0) return null;

            var cropped = new CroppedBitmap(_screenBitmap, new Int32Rect(pxX, pxY, pxW, pxH));

            if (AnnotationCanvas.Children.Count == 0)
            {
                cropped.Freeze();
                return cropped;
            }

            // Composite annotations onto the cropped bitmap with AnnotationCanvas bounds matching OverlayCanvas
            if (OverlayCanvas.ActualWidth > 0 && OverlayCanvas.ActualHeight > 0)
            {
                AnnotationCanvas.Width = OverlayCanvas.ActualWidth;
                AnnotationCanvas.Height = OverlayCanvas.ActualHeight;
                AnnotationCanvas.Measure(new Size(OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight));
                AnnotationCanvas.Arrange(new Rect(0, 0, OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight));
                AnnotationCanvas.UpdateLayout();
            }

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(cropped, new Rect(0, 0, _selectedDipRect.Width, _selectedDipRect.Height));
                var vb = new VisualBrush(AnnotationCanvas)
                {
                    Viewbox = _selectedDipRect,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Stretch = Stretch.None
                };
                dc.DrawRectangle(vb, null, new Rect(0, 0, _selectedDipRect.Width, _selectedDipRect.Height));
            }

            var rtb = new RenderTargetBitmap(pxW, pxH, 96 * _dpiScale, 96 * _dpiScale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
        catch (Exception ex)
        {
            App.Log($"[CaptureOverlayWindow] RenderCroppedCapture failed: {ex.Message}");
            return null;
        }
    }

    private void ToolBox_Click(object sender, RoutedEventArgs e) => _activeTool = AnnotationTool.Box;
    private void ToolArrow_Click(object sender, RoutedEventArgs e) => _activeTool = AnnotationTool.Arrow;
    private void ToolBlur_Click(object sender, RoutedEventArgs e) => _activeTool = AnnotationTool.Redact;

    private async void ActionOcr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var bmp = RenderCroppedCapture();
            Close();
            if (bmp != null)
            {
                string? text = await ScreenToolsService.ExtractTextFromBitmapAsync(bmp);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try { SafeClipboard.TrySetText(text); } catch { }
                    IslandWindow.Instance?.NotifyCaptureAction("OCR Text Copied", text);
                }
                else
                {
                    IslandWindow.Instance?.NotifyCaptureAction("No Text Detected", "Windows OCR found no readable text");
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[CaptureOverlayWindow] ActionOcr failed: {ex.Message}");
        }
    }

    private void ActionPin_Click(object sender, RoutedEventArgs e)
    {
        var bmp = RenderCroppedCapture();
        double targetLeft = Left + _selectedDipRect.X;
        double targetTop = Top + _selectedDipRect.Y;
        Close();
        if (bmp != null)
        {
            var pinWin = new PinnedCaptureWindow(bmp)
            {
                Left = targetLeft,
                Top = targetTop
            };
            pinWin.Show();
        }
    }

    private void ActionShelf_Click(object sender, RoutedEventArgs e)
    {
        var bmp = RenderCroppedCapture();
        Close();
        if (bmp != null)
        {
            string? saved = _shelfService.SaveBitmapToShelf(bmp);
            if (!string.IsNullOrEmpty(saved))
            {
                IslandWindow.Instance?.NotifyCaptureAction("Saved to Drop Shelf", System.IO.Path.GetFileName(saved));
            }
        }
    }

    private void ActionCopy_Click(object sender, RoutedEventArgs e)
    {
        var bmp = RenderCroppedCapture();
        Close();
        if (bmp != null)
        {
            try { Clipboard.SetImage(bmp); } catch { }
            IslandWindow.Instance?.NotifyCaptureAction("Capture Copied", $"{bmp.PixelWidth}×{bmp.PixelHeight} px");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
