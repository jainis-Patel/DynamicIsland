using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Views;

public partial class PinnedCaptureWindow : Window
{
    private readonly BitmapSource _bitmap;

    public PinnedCaptureWindow(BitmapSource bitmap)
    {
        _bitmap = bitmap;
        InitializeComponent();
        CaptureImage.Source = bitmap;
        double dipW = bitmap.DpiX > 0 ? bitmap.PixelWidth * 96.0 / bitmap.DpiX : bitmap.PixelWidth;
        double dipH = bitmap.DpiY > 0 ? bitmap.PixelHeight * 96.0 / bitmap.DpiY : bitmap.PixelHeight;
        Width = Math.Clamp(dipW + 16, 160, 640);
        Height = Math.Clamp(dipH + 16, 100, 480);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        MouseDoubleClick += (_, e) =>
        {
            Close();
            e.Handled = true;
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.08 : 0.92;
        Width = Math.Clamp(Width * factor, 120, 960);
        Height = Math.Clamp(Height * factor, 80, 720);
    }

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetImage(_bitmap); } catch { }
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange, 120, 1200);
        Height = Math.Clamp(Height + e.VerticalChange, 80, 900);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
