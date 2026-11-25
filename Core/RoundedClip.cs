using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland.Core;

/// <summary>
/// Event-driven attached behavior that clips a <see cref="Border"/>'s <see cref="Border.Child"/>
/// to the exact inner rounded rectangle of the Border (<c>R_inner = max(0, R_outer - BorderThickness)</c>).
/// Zero idle CPU: only updates on Loaded / SizeChanged and freezes the resulting geometry.
/// </summary>
public static class RoundedClip
{
    public static readonly DependencyProperty ClipChildProperty =
        DependencyProperty.RegisterAttached(
            "ClipChild",
            typeof(bool),
            typeof(RoundedClip),
            new PropertyMetadata(false, OnClipChildChanged));

    public static bool GetClipChild(DependencyObject obj) => (bool)obj.GetValue(ClipChildProperty);

    public static void SetClipChild(DependencyObject obj, bool value) => obj.SetValue(ClipChildProperty, value);

    private static void OnClipChildChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;

        bool enable = (bool)e.NewValue;
        border.Loaded -= OnBorderLoaded;
        border.SizeChanged -= OnBorderSizeChanged;
        border.Unloaded -= OnBorderUnloaded;

        if (enable)
        {
            border.Loaded += OnBorderLoaded;
            border.SizeChanged += OnBorderSizeChanged;
            border.Unloaded += OnBorderUnloaded;
            if (border.IsLoaded)
            {
                UpdateClip(border);
            }
        }
        else if (border.Child != null)
        {
            border.Child.Clip = null;
        }
    }

    private static void OnBorderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border border && GetClipChild(border))
        {
            border.SizeChanged -= OnBorderSizeChanged;
            border.SizeChanged += OnBorderSizeChanged;
            UpdateClip(border);
        }
    }

    private static void OnBorderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border && GetClipChild(border))
        {
            UpdateClip(border);
        }
    }

    private static void OnBorderUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.SizeChanged -= OnBorderSizeChanged;
        }
    }

    /// <summary>
    /// Computes and applies a frozen <see cref="RectangleGeometry"/> clip to <paramref name="border"/>'s child
    /// matching the Border's inner bounds and concentric corner radius.
    /// </summary>
    internal static void UpdateClip(Border border)
    {
        if (border.Child is not UIElement child) return;

        double w = Math.Max(0, border.ActualWidth - border.BorderThickness.Left - border.BorderThickness.Right);
        double h = Math.Max(0, border.ActualHeight - border.BorderThickness.Top - border.BorderThickness.Bottom);
        if (w <= 0 || h <= 0)
        {
            child.Clip = null;
            return;
        }

        double rx = Math.Max(0, border.CornerRadius.TopLeft - border.BorderThickness.Left);
        double ry = Math.Max(0, border.CornerRadius.TopLeft - border.BorderThickness.Top);

        var geo = new RectangleGeometry(new Rect(0, 0, w, h), rx, ry);
        geo.Freeze();
        child.Clip = geo;
    }
}
