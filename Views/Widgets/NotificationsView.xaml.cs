using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class NotificationsView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private Point? _dragStart;
    private Border? _draggedCard;
    private bool _isDragging;
    private DateTime _dragStartTime;

    public NotificationsView()
    {
        InitializeComponent();
    }

    private void ClearNotificationsButton_Click(object sender, RoutedEventArgs e) =>
        Island?.ClearNotificationsButton_Click(sender, e);

    private void MuteAppNotification_Click(object sender, RoutedEventArgs e) =>
        Island?.MuteAppNotification_Click(sender, e);

    private void NotificationCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        Island?.NotificationCard_Click(sender, e);
    }

    private void SubNotificationCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.SubNotificationCard_Click(sender, e);

    private void ReplyNotificationItem_Click(object sender, RoutedEventArgs e) =>
        Island?.ReplyNotificationItem_Click(sender, e);

    private void RemoveNotificationItem_Click(object sender, RoutedEventArgs e) =>
        Island?.RemoveNotificationItem_Click(sender, e);

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void UnmuteAllAppsButton_Click(object sender, RoutedEventArgs e) =>
        Island?.UnmuteAllAppsButton_Click(sender, e);

    private void ToggleGroupingButton_Click(object sender, RoutedEventArgs e) =>
        Island?.ToggleNotificationGrouping();

    private void ToggleGroupExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NotificationAppGroup group })
        {
            Island?.ToggleNotificationAppGroup(group);
            e.Handled = true;
        }
    }

    // -------------------------------------------------------------------------
    //  Interactive Swipe / Flick-to-Dismiss Gestures
    // -------------------------------------------------------------------------

    private void NotificationCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't intercept clicks destined for buttons inside the card (Reply, Dismiss, Mute, Chevron)
        if (FindVisualParent<Button>(e.OriginalSource as DependencyObject) != null)
        {
            _draggedCard = null;
            return;
        }

        if (sender is Border border)
        {
            _draggedCard = border;
            _dragStart = e.GetPosition(this);
            _dragStartTime = DateTime.UtcNow;
            _isDragging = false;
        }
    }

    private void NotificationCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedCard != null && _dragStart != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(this);
            double dx = current.X - _dragStart.Value.X;
            double dy = current.Y - _dragStart.Value.Y;

            if (!_isDragging)
            {
                // Initiate swipe drag only if horizontal intent dominates
                if (Math.Abs(dx) > 10 && Math.Abs(dx) > Math.Abs(dy) * 1.2)
                {
                    _isDragging = true;
                    _draggedCard.CaptureMouse();
                }
            }

            if (_isDragging && _draggedCard.RenderTransform is TranslateTransform tt)
            {
                tt.X = dx;
                double progress = Math.Clamp(Math.Abs(dx) / 250.0, 0.0, 1.0);
                _draggedCard.Opacity = 1.0 - (progress * 0.55);
                e.Handled = true;
            }
        }
    }

    private void NotificationCard_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedCard != null)
        {
            var card = _draggedCard;
            bool wasDragging = _isDragging;
            _draggedCard = null;
            _isDragging = false;

            if (wasDragging && card.RenderTransform is TranslateTransform tt)
            {
                card.ReleaseMouseCapture();
                var current = e.GetPosition(this);
                double dx = _dragStart != null ? current.X - _dragStart.Value.X : 0;
                var elapsed = DateTime.UtcNow - _dragStartTime;
                double velocity = elapsed.TotalSeconds > 0 ? Math.Abs(dx) / elapsed.TotalSeconds : 0;

                // Threshold: dragged past 75px or flicked quickly past 35px
                bool dismiss = Math.Abs(dx) > 75 || (velocity > 550 && Math.Abs(dx) > 35);
                if (dismiss)
                {
                    double targetX = dx >= 0 ? 500 : -500;
                    var animX = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(160))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    var animOpacity = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));

                    animX.Completed += (s, args) =>
                    {
                        tt.X = 0;
                        card.Opacity = 1.0;

                        if (card.DataContext is NotificationAppGroup group)
                        {
                            Island?.DismissNotificationGroup(group);
                        }
                        else if (card.DataContext is NotificationHistoryItem item)
                        {
                            Island?.RemoveNotificationItem_Click(new FrameworkElement { Tag = item.Id }, new RoutedEventArgs());
                        }
                    };

                    tt.BeginAnimation(TranslateTransform.XProperty, animX);
                    card.BeginAnimation(UIElement.OpacityProperty, animOpacity);
                    e.Handled = true;
                    _dragStart = null;
                    return;
                }
                else
                {
                    // Spring bounce back to center
                    var bounceX = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut }
                    };
                    var restoreOpacity = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160));

                    tt.BeginAnimation(TranslateTransform.XProperty, bounceX);
                    card.BeginAnimation(UIElement.OpacityProperty, restoreOpacity);
                    e.Handled = true;
                    _dragStart = null;
                    return;
                }
            }
        }
        _dragStart = null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}