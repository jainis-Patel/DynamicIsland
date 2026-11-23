using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Core;

/// <summary>
/// Provisions an independent, unfrozen <see cref="ScaleTransform"/> on each styled Bento card instance
/// so WPF Storyboard hover/press triggers animate individual cards without cross-card transform sharing.
/// </summary>
internal static class BentoMotion
{
    private static readonly IEasingFunction SharedCubicOut = CreateFrozenEase(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction SharedBackOut = CreateFrozenEase(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });
    private static readonly IEasingFunction SharedPressBackOut = CreateFrozenEase(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });

    private static IEasingFunction CreateFrozenEase(EasingFunctionBase ease)
    {
        ease.Freeze();
        return ease;
    }

    public static readonly DependencyProperty EnableSpringProperty = DependencyProperty.RegisterAttached(
        "EnableSpring",
        typeof(bool),
        typeof(BentoMotion),
        new PropertyMetadata(false, OnEnableSpringChanged));

    public static bool GetEnableSpring(DependencyObject obj) => (bool)obj.GetValue(EnableSpringProperty);

    public static void SetEnableSpring(DependencyObject obj, bool value) => obj.SetValue(EnableSpringProperty, value);

    private static void OnEnableSpringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element && e.NewValue is true)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            if (element.RenderTransform is not ScaleTransform || element.RenderTransform.IsFrozen)
            {
                element.RenderTransform = new ScaleTransform(1.0, 1.0);
            }
        }
    }

    public static void AttachInteractiveSpring(UIElement? element, double hoverScale = 1.04, double pressScale = 0.94)
    {
        if (element == null) return;

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        if (element.RenderTransform is not ScaleTransform st || st.IsFrozen)
        {
            st = new ScaleTransform(1.0, 1.0);
            element.RenderTransform = st;
        }

        void AnimateTo(double scale, int ms, IEasingFunction? ease)
        {
            var anim = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = ease
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        element.MouseEnter += (_, _) => AnimateTo(hoverScale, 140, SharedCubicOut);
        element.MouseLeave += (_, _) => AnimateTo(1.0, 220, SharedBackOut);
        element.PreviewMouseLeftButtonDown += (_, _) => AnimateTo(pressScale, 70, null);
        element.PreviewMouseLeftButtonUp += (_, _) => AnimateTo(element.IsMouseOver ? hoverScale : 1.0, 220, SharedPressBackOut);
    }
}
