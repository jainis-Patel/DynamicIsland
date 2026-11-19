using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class WidgetsGalleryView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? (Window.GetWindow(this) as IslandWindow) ?? (Application.Current?.MainWindow as IslandWindow);

    private bool _isExpanded;

    public WidgetsGalleryView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshAllWidgets();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SetExpanded(false);
    }

    public void SetExpanded(bool isExpanded)
    {
        if (_isExpanded == isExpanded) return;
        _isExpanded = isExpanded;

        var island = Island;
        if (island == null) return;

        if (_isExpanded)
        {
            // Subscribe to live telemetry strictly while expanded (Zero Idle CPU invariant)
            if (island.SystemPerfServiceInstance is { } sys)
            {
                sys.VitalsUpdated -= OnVitalsUpdated;
                sys.VitalsUpdated += OnVitalsUpdated;
                sys.SetHudExpanded(true);
            }

            if (island.NetworkServiceInstance is { } net)
            {
                net.Updated -= OnNetworkUpdated;
                net.Updated += OnNetworkUpdated;
                net.SetExpanded(true);
            }

            if (island.PowerPlanServiceInstance is { } pwr)
            {
                pwr.StatusChanged -= OnPowerStatusChanged;
                pwr.StatusChanged += OnPowerStatusChanged;
            }

            if (island.MediaServiceInstance is { } media)
            {
                media.Changed -= OnMediaChanged;
                media.Changed += OnMediaChanged;
                media.TrackChanged -= OnTrackChanged;
                media.TrackChanged += OnTrackChanged;
            }

            RefreshAllWidgets();
        }
        else
        {
            // Unhook all event handlers immediately on collapse to ensure 0% CPU consumption
            if (island.SystemPerfServiceInstance is { } sys)
            {
                sys.VitalsUpdated -= OnVitalsUpdated;
                sys.SetHudExpanded(false);
            }

            if (island.NetworkServiceInstance is { } net)
            {
                net.Updated -= OnNetworkUpdated;
                net.SetExpanded(false);
            }

            if (island.PowerPlanServiceInstance is { } pwr)
            {
                pwr.StatusChanged -= OnPowerStatusChanged;
            }

            if (island.MediaServiceInstance is { } media)
            {
                media.Changed -= OnMediaChanged;
                media.TrackChanged -= OnTrackChanged;
            }
        }
    }

    public void RefreshAllWidgets()
    {
        var island = Island;
        if (island == null) return;

        // 1. System Vitals
        if (island.SystemPerfServiceInstance is { } sys)
        {
            if (sys.LatestSnapshot is { } snap)
            {
                UpdateVitalsUI(snap);
            }
            if (!string.IsNullOrWhiteSpace(sys.HardwareSummary) && HardwareSummaryText != null)
            {
                HardwareSummaryText.Text = sys.HardwareSummary;
            }
        }

        // 2. Battery & Power
        if (island.PowerPlanServiceInstance is { } pwr)
        {
            UpdateBatteryUI(pwr.Current);
        }

        // 3. Network & Wi-Fi
        if (island.NetworkServiceInstance is { } net)
        {
            UpdateNetworkUI(net.Current);
        }

        // 4. Media & Now Playing
        if (island.MediaServiceInstance is { } media)
        {
            UpdateMediaUI(media.Current);
        }

        // 5. Calendar & Tasks
        UpdateCalendarUI();

        // 6. Notes Scratchpad
        UpdateNotesUI();

        // 7. Drop Shelf
        UpdateShelfUI();
    }

    #region Telemetry Callbacks
    private void OnVitalsUpdated(SystemVitalsSnapshot snap)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateVitalsUI(snap));
    }

    private void OnNetworkUpdated(NetworkStatusSnapshot snap)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateNetworkUI(snap));
    }

    private void OnPowerStatusChanged(PowerStatusInfo status)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateBatteryUI(status));
    }

    private void OnMediaChanged()
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateMediaUI(Island?.MediaServiceInstance?.Current));
    }

    private void OnTrackChanged()
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateMediaUI(Island?.MediaServiceInstance?.Current));
    }
    #endregion

    #region UI Updaters
    private void UpdateVitalsUI(SystemVitalsSnapshot snap)
    {
        if (CpuPercentBadge != null)
            CpuPercentBadge.Text = $"CPU {snap.CpuPercent}%";

        if (CpuDetailText != null)
            CpuDetailText.Text = $"{snap.CpuPercent}%";

        if (RamDetailText != null)
            RamDetailText.Text = $"{snap.RamUsedGb:0.0} / {snap.RamTotalGb:0} GB";

        if (CpuMiniBar != null)
            CpuMiniBar.Width = Math.Clamp((snap.CpuPercent / 100.0) * 80.0, 4.0, 80.0);

        if (RamMiniBar != null)
            RamMiniBar.Width = Math.Clamp((snap.RamPercent / 100.0) * 80.0, 4.0, 80.0);
    }

    private void UpdateBatteryUI(PowerStatusInfo status)
    {
        if (status == null) return;

        if (BatteryBigPercent != null)
            BatteryBigPercent.Text = status.HasBattery ? $"{status.BatteryPercent}%" : "AC";

        if (BatteryStatusText != null)
        {
            if (status.IsCharging)
                BatteryStatusText.Text = " · Charging";
            else if (status.IsAcOnline)
                BatteryStatusText.Text = " · Plugged in";
            else
                BatteryStatusText.Text = " · On Battery";
        }

        if (PowerPlanBadge != null)
        {
            string plan = string.IsNullOrWhiteSpace(status.ActiveSchemeName) ? "Balanced" : status.ActiveSchemeName;
            PowerPlanBadge.Text = plan;
        }
    }

    private void UpdateNetworkUI(NetworkStatusSnapshot snap)
    {
        if (snap == null) return;

        if (NetDownloadSpeed != null)
            NetDownloadSpeed.Text = snap.DownloadSpeedText;

        if (NetUploadSpeed != null)
            NetUploadSpeed.Text = snap.UploadSpeedText;

        if (NetworkNameText != null)
        {
            string name = !string.IsNullOrWhiteSpace(snap.ConnectionName)
                ? snap.ConnectionName
                : (snap.IsConnected ? "Connected" : "Disconnected");
            NetworkNameText.Text = $"{name} · {snap.LocalIpv4}";
        }

        if (NetworkPingBadge != null)
        {
            NetworkPingBadge.Text = snap.PingMs > 0 ? $"{snap.PingMs}ms" : (snap.IsConnected ? "Active" : "Offline");
        }
    }

    private void UpdateMediaUI(MediaInfo? media)
    {
        bool hasMedia = media != null && !string.IsNullOrWhiteSpace(media.Title);

        if (TrackTitleText != null)
            TrackTitleText.Text = hasMedia ? media!.Title : "No media playing";

        if (TrackArtistText != null)
            TrackArtistText.Text = hasMedia ? (string.IsNullOrWhiteSpace(media!.Artist) ? "Unknown Artist" : media.Artist) : "Open Spotify, Apple Music, or YouTube Music";

        if (PlayPauseMiniGlyph != null)
            PlayPauseMiniGlyph.Text = (media?.IsPlaying ?? false) ? "\uE769" : "\uE768";

        if (PlayerBrandBadgeHost != null && PlayerBrandBadgeText != null)
        {
            string brand = media?.BrandInfo?.DisplayName ?? "";
            if (!string.IsNullOrWhiteSpace(brand))
            {
                PlayerBrandBadgeText.Text = brand;
                PlayerBrandBadgeHost.Visibility = Visibility.Visible;
            }
            else
            {
                PlayerBrandBadgeHost.Visibility = Visibility.Collapsed;
            }
        }

        if (MediaThumbnailImage != null && MediaFallbackGlyph != null)
        {
            if (media?.Artwork != null)
            {
                MediaThumbnailImage.Source = media.Artwork;
                MediaThumbnailImage.Visibility = Visibility.Visible;
                MediaFallbackGlyph.Visibility = Visibility.Collapsed;
            }
            else
            {
                MediaThumbnailImage.Visibility = Visibility.Collapsed;
                MediaFallbackGlyph.Visibility = Visibility.Visible;
            }
        }
    }

    private void UpdateCalendarUI()
    {
        if (TodayDateBadge != null)
        {
            TodayDateBadge.Text = DateTime.Now.ToString("ddd · MMM d").ToUpperInvariant();
        }

        var island = Island;
        if (island?.CalendarServiceInstance is { } cal)
        {
            if (cal.NextEvent is { } next)
            {
                if (NextEventTitleText != null) NextEventTitleText.Text = next.Subject;
                if (NextEventTimeText != null) NextEventTimeText.Text = next.TimeBadgeText;
                return;
            }
        }

        if (NextEventTitleText != null) NextEventTitleText.Text = "All caught up";
        if (NextEventTimeText != null) NextEventTimeText.Text = "No upcoming meetings today";
    }

    private void UpdateNotesUI()
    {
        var island = Island;
        if (island?.NotesServiceInstance is { } notes && NotesSnippetText != null)
        {
            var note = notes.Notes.Count > 0 ? notes.Notes[0] : null;
            if (note != null)
            {
                string text = !string.IsNullOrWhiteSpace(note.Title) ? note.Title : note.Content;
                string firstLine = text.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                NotesSnippetText.Text = firstLine.Length > 28 ? firstLine.Substring(0, 28) + "..." : firstLine;
            }
            else
            {
                NotesSnippetText.Text = "Scratchpad & Checklist";
            }
        }
    }

    private void UpdateShelfUI()
    {
        var island = Island;
        if (island?.DropShelfServiceInstance is { } shelf && ShelfCountBadge != null)
        {
            ShelfCountBadge.Text = shelf.Count == 1 ? "1 file" : $"{shelf.Count} files";
            if (ShelfStatusText != null)
                ShelfStatusText.Text = shelf.Count > 0 ? $"{shelf.Count} items staged" : "Temporary Staged Tray";
        }
    }
    #endregion

    #region Navigation Handlers
    private void SystemVitalsCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.SystemHud);

    private void BatteryCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Battery);

    private void NetworkCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Network);

    private void CalendarCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Calendar);

    private void PeripheralsCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Peripherals);

    private void NotesCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Notes);

    private void CalcCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Calc);

    private void ShelfCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Shelf);

    private void VoiceMemoCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.VoiceMemo);

    private void CaptureStudioCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.CaptureStudio);

    private void ConverterCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Converter);

    private void TermiCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Termi);

    private void PhoneBridgeCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.PhoneBridge);

    private void MechSoundsCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.MechSounds);

    private void ClipboardCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Clipboard);

    private void MediaCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Media);

    private void OpenLaunchpad_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Launcher);

    private void PlayPauseMiniBtn_Click(object sender, RoutedEventArgs e)
    {
        Island?.MediaServiceInstance?.TogglePlayPause();
        e.Handled = true;
    }

    public void Scroll(double delta)
    {
        if (WidgetsScrollViewer != null)
        {
            double target = Math.Clamp(WidgetsScrollViewer.VerticalOffset + delta, 0, Math.Max(0, WidgetsScrollViewer.ScrollableHeight));
            WidgetsScrollViewer.ScrollToVerticalOffset(target);
        }
    }

    private void WidgetsGallery_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (WidgetsScrollViewer != null)
        {
            double step = e.Delta > 0 ? -64.0 : 64.0;
            double target = Math.Clamp(WidgetsScrollViewer.VerticalOffset + step, 0, Math.Max(0, WidgetsScrollViewer.ScrollableHeight));
            WidgetsScrollViewer.ScrollToVerticalOffset(target);
            e.Handled = true;
        }
    }
    #endregion
}
