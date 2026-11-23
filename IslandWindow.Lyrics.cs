using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    // =================================================================
    // Synced Karaoke Lyrics (LRCLIB) & Network HUD Event Handlers
    // =================================================================

    private void OnLyricsLoaded()
    {
        if (_lyricsService == null) return;
        bool hasLyrics = _lyricsService.HasLyrics;
        bool lyricsEnabled = _settingsService?.Current?.EnableSyncedLyrics ?? true;

        if (hasLyrics && lyricsEnabled)
        {
            if (MediaLyricsBadge != null)
            {
                MediaLyricsBadge.Visibility = Visibility.Visible;
                MediaLyricsBadge.Opacity = 1.0;
            }
            if (MediaLyricsBadgeText != null) MediaLyricsBadgeText.Text = "Lyrics";
            if (KaraokeLyricsHost != null) KaraokeLyricsHost.Visibility = Visibility.Visible;

            var media = _mediaService?.Current;
            if (media != null)
            {
                _lyricsService.UpdatePosition(media.GetPosition(DateTimeOffset.Now));
            }
        }
        else if (_lyricsService.IsLoading)
        {
            if (MediaLyricsBadge != null)
            {
                MediaLyricsBadge.Visibility = Visibility.Visible;
                MediaLyricsBadge.Opacity = 0.65;
            }
            if (MediaLyricsBadgeText != null) MediaLyricsBadgeText.Text = "Searching...";
            if (KaraokeLyricsHost != null) KaraokeLyricsHost.Visibility = Visibility.Visible;
            if (LyricCurrentLine != null) LyricCurrentLine.Text = "\u266A Searching lyrics...";
            if (LyricPrevLine != null) LyricPrevLine.Text = "";
            if (LyricNextLine != null) LyricNextLine.Text = "";
        }
        else
        {
            if (MediaLyricsBadge != null)
            {
                MediaLyricsBadge.Visibility = lyricsEnabled ? Visibility.Visible : Visibility.Collapsed;
                MediaLyricsBadge.Opacity = 0.45;
            }
            if (MediaLyricsBadgeText != null) MediaLyricsBadgeText.Text = "No Lyrics";
            if (LyricCurrentLine != null)
            {
                LyricCurrentLine.Text = _lyricsService.IsInstrumental ? "\u266A Instrumental Track" : "\u266A No synced lyrics available";
            }
            if (LyricPrevLine != null) LyricPrevLine.Text = "";
            if (LyricNextLine != null) LyricNextLine.Text = "";
        }

        SyncHomeCenterStageVisibility();

        if (_machine.State == IslandState.Expanded)
        {
            ApplyGeometry();
            EnsureRendering();
        }
    }

    private void OnLyricsLineChanged(string prev, string current, string next, int index)
    {
        if (LyricPrevLine != null) LyricPrevLine.Text = prev;
        if (LyricCurrentLine != null)
        {
            if (!string.IsNullOrWhiteSpace(current))
            {
                LyricCurrentLine.Text = current;
            }
            else if (_lyricsService?.HasLyrics != true)
            {
                LyricCurrentLine.Text = _lyricsService?.IsInstrumental == true
                    ? "\u266A Instrumental Track"
                    : "\u266A No synced lyrics available";
            }
        }
        if (LyricNextLine != null) LyricNextLine.Text = next;

        if (_settingsService?.Current?.ShowLyricsInCompact == true && !string.IsNullOrWhiteSpace(current))
        {
            CompactTrackText.Text = current;
        }
        else
        {
            var media = _mediaService.Current;
            if (media != null && !string.IsNullOrWhiteSpace(media.Title))
            {
                CompactTrackText.Text = media.Title;
            }
        }
    }

    internal void MediaLyricsBadge_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        bool currentEnabled = _settingsService?.Current?.EnableSyncedLyrics ?? true;

        // If lyrics are already enabled but no lyrics are showing (e.g. initial failure or "No Lyrics"),
        // clicking the badge forces a re-query and informs the user via toast!
        if (currentEnabled && !_lyricsService.HasLyrics)
        {
            var media = _mediaService.Current;
            if (media != null && !string.IsNullOrWhiteSpace(media.Title))
            {
                var (cleanTitle, _) = LyricsService.ParseTrackMetadata(media.Title, media.Artist);
                _lyricsService.LoadLyricsForTrack(media.Title, media.Artist, media.Duration, forceRetry: true);
                _machine.PushToast(new ToastContent(
                    "\uEC4F",
                    "Searching Lyrics",
                    $"Querying LRCLIB for \"{cleanTitle}\"...",
                    ThemeBrushes.BlueColor,
                    TimeSpan.FromSeconds(2.5),
                    TargetView: "media",
                    Priority: ToastPriority.Feedback,
                    Category: "lyrics"));
                return;
            }
        }

        bool newEnabled = !currentEnabled;
        _settingsService?.Update(s => s.EnableSyncedLyrics = newEnabled);

        if (!newEnabled)
        {
            MediaLyricsBadge.Opacity = 0.4;
        }
        else
        {
            var media = _mediaService.Current;
            if (media != null && !string.IsNullOrWhiteSpace(media.Title))
            {
                _lyricsService.LoadLyricsForTrack(media.Title, media.Artist, media.Duration, forceRetry: true);
            }
            MediaLyricsBadge.Opacity = 1.0;
        }

        SyncHomeCenterStageVisibility();
        UpdateLyricsSettingsUI();
        ApplyGeometry();
        EnsureRendering();
    }

    internal void LyricsToggleBtnSettings_Click(object sender, RoutedEventArgs e)
    {
        bool current = _settingsService.Current.EnableSyncedLyrics;
        bool next = !current;
        _settingsService.Update(s => s.EnableSyncedLyrics = next, nameof(AppSettings.EnableSyncedLyrics));
        _machine.PushToast(new ToastContent(
            next ? "\uE8D6" : "\uE711",
            "Synced Lyrics",
            next ? "Karaoke lyrics enabled (LRCLIB)" : "Karaoke lyrics disabled",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "lyrics"));
    }

    internal void CompactLyricsToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool current = _settingsService.Current.ShowLyricsInCompact;
        bool next = !current;
        _settingsService.Update(s => s.ShowLyricsInCompact = next, nameof(AppSettings.ShowLyricsInCompact));
        _machine.PushToast(new ToastContent(
            next ? "\uEC4F" : "\uE8D6",
            "Compact Pill Lyrics",
            next ? "Showing live lyric lines in compact pill" : "Showing track title in compact pill",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "lyrics"));
    }

    internal void NetSpeedIdleToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool current = _settingsService.Current.ShowNetworkSpeedInIdle;
        bool next = !current;
        _settingsService.Update(s => s.ShowNetworkSpeedInIdle = next, nameof(AppSettings.ShowNetworkSpeedInIdle));
        _machine.PushToast(new ToastContent(
            next ? "\uE896" : "\uE711",
            "Internet Speed HUD",
            next ? "Live throughput in desktop pill enabled" : "Throughput HUD hidden from desktop pill",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "network",
            Priority: ToastPriority.Feedback,
            Category: "netspeed"));
    }

    private void IdleNetSpeed_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _machine.ExpandNow();
        SwitchExpandedView(ExpandedView.Network);
    }

    internal void CameraLensToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool cur = _settingsService.Current.ShowCameraLensIcon;
        _settingsService.Update(s => s.ShowCameraLensIcon = !cur, nameof(AppSettings.ShowCameraLensIcon));
        _machine.PushToast(new ToastContent(
            !cur ? "\uE722" : "\uE711",
            !cur ? "Camera Dot Enabled" : "Camera Dot Hidden",
            !cur ? "Simulated camera lens dot visible in idle pill" : "Camera lens dot hidden from idle pill",
            !cur ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "capsule"));
    }

    private void UpdateCapsuleElementsUI()
    {
        bool camOn = _settingsService?.Current.ShowCameraLensIcon ?? true;
        var cp = SettingsContainer?.ClockAudioPanel;
        if (cp?.CameraLensToggleSwitch != null)
        {
            cp.CameraLensToggleSwitch.IsChecked = camOn;
        }

        OnPrivacyStateChanged();
    }

    private void UpdateLyricsSettingsUI()
    {
        var cp = SettingsContainer?.ClockAudioPanel;
        bool lyricsOn = _settingsService?.Current?.EnableSyncedLyrics ?? true;
        if (cp?.LyricsToggleSwitch != null)
        {
            cp.LyricsToggleSwitch.IsChecked = lyricsOn;
        }

        bool compactLyricsOn = _settingsService?.Current?.ShowLyricsInCompact ?? false;
        if (cp?.CompactLyricsToggleSwitch != null)
        {
            cp.CompactLyricsToggleSwitch.IsChecked = compactLyricsOn;
        }
    }

    private void UpdateNetSpeedIdleUI()
    {
        bool netOn = _settingsService?.Current?.ShowNetworkSpeedInIdle ?? false;
        var cp = SettingsContainer?.ClockAudioPanel;
        if (cp?.NetSpeedIdleToggleSwitch != null)
        {
            cp.NetSpeedIdleToggleSwitch.IsChecked = netOn;
        }

        // Network Hub container button
        if (NetworkPillSpeedToggleText != null)
        {
            NetworkPillSpeedToggleText.Text = netOn ? "Pill Speed On" : "Pill Speed Off";
            NetworkPillSpeedToggleText.Foreground = netOn ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        }
        if (NetworkPillSpeedToggleBtn != null)
        {
            NetworkPillSpeedToggleBtn.BorderBrush = netOn
                ? ThemeBrushes.GreenBorderActive
                : Brushes.Transparent;
        }

        // Pill host element
        if (IdleNetSpeedHost != null)
        {
            IdleNetSpeedHost.Visibility = netOn ? Visibility.Visible : Visibility.Collapsed;
        }

        // When toggled on, immediately show current throughput if available
        if (netOn && _networkService?.Current != null)
        {
            var snap = _networkService.Current;
            if (IdleNetSpeedText != null)
            {
                IdleNetSpeedText.Text = snap.IsConnected ? snap.ShortSpeedText : "\u2014";
                IdleNetSpeedText.Foreground = snap.IsConnected ? ThemeBrushes.SecondaryText : ThemeBrushes.Amber;
            }
            if (IdleNetSpeedGlyph != null)
            {
                IdleNetSpeedGlyph.Foreground = snap.IsConnected ? ThemeBrushes.Green : ThemeBrushes.Amber;
                IdleNetSpeedGlyph.Text = snap.IsConnected ? "\uE896" : "\uEB5E";
            }
        }
    }
}
