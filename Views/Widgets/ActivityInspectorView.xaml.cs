using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Views.Widgets;

public sealed class ActivityInspectorRow
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Glyph { get; init; }
    public required Brush AccentBrush { get; init; }
    public required double ProgressPercent { get; init; }
    public required string ProgressText { get; init; }
    public required Visibility ProgressVisibility { get; init; }
    public required string JoinedLogs { get; init; }
    public required Visibility LogsVisibility { get; init; }
}

public partial class ActivityInspectorView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? (Window.GetWindow(this) as IslandWindow);

    public ActivityInspectorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Island?.LiveActivityHubInstance is { } hub)
        {
            hub.Changed -= OnHubChanged;
            hub.Changed += OnHubChanged;
        }
        RefreshList();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Island?.LiveActivityHubInstance is { } hub)
        {
            hub.Changed -= OnHubChanged;
        }
    }

    private void OnHubChanged() => Dispatcher.InvokeAsync(RefreshList);

    public void RefreshList()
    {
        var hub = Island?.LiveActivityHubInstance;
        var items = hub?.Activities ?? Array.Empty<LiveActivityItem>();

        CountBadgeText.Text = items.Count.ToString();
        ClearAllBtn.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPlaceholder.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var rows = new List<ActivityInspectorRow>(items.Count);
        foreach (var item in items)
        {
            Brush accent = item.AccentColor != default
                ? ThemeBrushes.CreateFrozen(item.AccentColor)
                : ThemeBrushes.Blue;
            rows.Add(new ActivityInspectorRow
            {
                Id = item.Id,
                Source = string.IsNullOrWhiteSpace(item.Source) ? "Activity" : item.Source,
                Title = item.Title,
                Subtitle = item.Subtitle,
                Glyph = string.IsNullOrWhiteSpace(item.Glyph) ? "\uE7E7" : item.Glyph,
                AccentBrush = accent,
                ProgressPercent = item.Progress.HasValue ? item.ClampedProgress * 100.0 : 0.0,
                ProgressText = item.ProgressPercentText,
                ProgressVisibility = item.Progress.HasValue ? Visibility.Visible : Visibility.Collapsed,
                JoinedLogs = item.JoinedLogs,
                LogsVisibility = item.HasLogs ? Visibility.Visible : Visibility.Collapsed
            });
        }
        ActivitiesItemsControl.ItemsSource = rows;
    }

    private void DismissActivityBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && !string.IsNullOrWhiteSpace(id))
        {
            Island?.LiveActivityHubInstance?.Complete(id);
            RefreshList();
        }
    }

    private void ClearAllBtn_Click(object sender, RoutedEventArgs e)
    {
        var hub = Island?.LiveActivityHubInstance;
        if (hub == null) return;
        foreach (var item in hub.Activities)
        {
            hub.Complete(item.Id);
        }
        RefreshList();
    }
}
