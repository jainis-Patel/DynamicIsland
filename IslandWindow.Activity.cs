using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    // ═════════════════════════════════════════════════════════════════
    //  Activity & Clock/Timeline Coordination
    // ═════════════════════════════════════════════════════════════════

    private void EvaluateActivity()
    {
        bool mediaPlaying = _mediaService.Current?.IsPlaying ?? false;
        bool stopwatchActive = _stopwatch.IsRunning;
        bool countdownRunning = _countdownService?.IsRunning ?? false;
        bool callActive = !string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp);
        bool downloading = _downloadService.HasActiveDownloads;
        bool recording = _voiceMemoService?.IsRecording ?? false;
        bool liveActivity = _liveActivityHub?.HasActivities ?? false;

        var (hasActivity, host, secondaryHost) = ActivityResolver.ResolveWithSecondary(
            recording,
            mediaPlaying,
            callActive,
            countdownRunning,
            downloading,
            stopwatchActive,
            liveActivity);

        bool enableSecondary = _settingsService?.Current.EnableSecondaryDroplet ?? true;
        if (!enableSecondary)
        {
            secondaryHost = CompactActivityKind.None;
        }

        _activeCompactHost = host;
        _secondaryCompactHost = secondaryHost;
        _machine.SetActivity(hasActivity);

        bool showTimerHost = host is CompactActivityKind.Countdown or CompactActivityKind.Stopwatch or CompactActivityKind.None;
        CompactRecordHost.Visibility   = host == CompactActivityKind.Recording ? Visibility.Visible : Visibility.Collapsed;
        CompactMediaHost.Visibility    = host == CompactActivityKind.Media ? Visibility.Visible : Visibility.Collapsed;
        CompactCallHost.Visibility     = host == CompactActivityKind.Call ? Visibility.Visible : Visibility.Collapsed;
        CompactDownloadHost.Visibility = host == CompactActivityKind.Download ? Visibility.Visible : Visibility.Collapsed;
        CompactActivityHost.Visibility = host == CompactActivityKind.LiveActivity ? Visibility.Visible : Visibility.Collapsed;
        CompactTimerHost.Visibility    = showTimerHost ? Visibility.Visible : Visibility.Collapsed;

        if (host == CompactActivityKind.LiveActivity && _liveActivityHub?.Primary is { } primary)
        {
            CompactActivityTitle.Text = primary.Title;
            if (!string.IsNullOrWhiteSpace(primary.Subtitle))
            {
                CompactActivitySubtitle.Text = primary.Subtitle;
                CompactActivitySubtitle.Visibility = Visibility.Visible;
            }
            else
            {
                CompactActivitySubtitle.Visibility = Visibility.Collapsed;
            }

            if (!string.IsNullOrWhiteSpace(primary.Glyph))
            {
                CompactActivityGlyph.Text = primary.Glyph;
            }

            if (primary.AccentColor != default)
            {
                var brush = new SolidColorBrush(primary.AccentColor);
                brush.Freeze();
                CompactActivityGlyph.Foreground = brush;
                var badgeBg = new SolidColorBrush(Color.FromArgb(0x28, primary.AccentColor.R, primary.AccentColor.G, primary.AccentColor.B));
                badgeBg.Freeze();
                CompactActivityBadge.Background = badgeBg;
            }

            if (primary.Progress.HasValue)
            {
                CompactActivityTrailingBadge.Visibility = Visibility.Visible;
                CompactActivityTrailingText.Text = primary.ProgressPercentText;
            }
            else
            {
                CompactActivityTrailingBadge.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            CompactActivityTrailingBadge.Visibility = Visibility.Collapsed;
        }

        UpdateSecondaryDropletTarget();
        UpdateSpectrumActivity();
        UpdateClockTimerInterval();
    }

    private void UpdateClockTimerInterval()
    {
        string currentStyle = SettingsService.NormalizeClockStyle(_settingsService?.Current?.ClockStyle);
        bool mediaPlaying = _mediaService.Current?.IsPlaying ?? false;
        bool timerRunning = _stopwatch.IsRunning || (_countdownService?.IsRunning ?? false);
        bool expandedMediaActive = _machine.State == IslandState.Expanded &&
                                   _currentExpandedView == ExpandedView.Media &&
                                   (mediaPlaying || timerRunning);
        bool compactLyricsActive = _machine.State == IslandState.Compact &&
                                   (_settingsService?.Current?.ShowLyricsInCompact ?? false) &&
                                   mediaPlaying;
        bool idleAnalogActive = _machine.State == IslandState.Idle &&
                                (currentStyle == "Analog" || currentStyle == "JumpHour" || currentStyle == "Matrix");

        bool needsSubSecond = expandedMediaActive || compactLyricsActive || idleAnalogActive;

        // 50ms (20Hz) only when active media scrubber/lyrics or analog sweep hand is visible; 1s (1Hz) otherwise
        var targetInterval = needsSubSecond ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(1);
        if (_clockTimer != null && _clockTimer.Interval != targetInterval)
        {
            _clockTimer.Interval = targetInterval;
        }
    }

    private void UpdateClockAndTimeline()
    {
        var now = DateTime.Now;
        string time = now.ToString("t", CultureInfo.CurrentCulture);
        if (time != _lastTimeText)
        {
            _lastTimeText = time;
            if (IdleClock != null) IdleClock.Text = time;
            if (IdleClockSecHMin != null) IdleClockSecHMin.Text = time;
            if (IdleClockDateHMin != null) IdleClockDateHMin.Text = time;
            if (IdleClockAnalogHMin != null) IdleClockAnalogHMin.Text = time;
            if (IdleClockNeon != null) IdleClockNeon.Text = time;
            if (IdleClockNeonGlow != null) IdleClockNeonGlow.Text = time;
            if (IdleClockMatrixHMin != null) IdleClockMatrixHMin.Text = time;

            string hourFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H") ? "HH" : "hh";
            if (IdleFlipHourText != null) IdleFlipHourText.Text = now.ToString(hourFormat, CultureInfo.CurrentCulture);
            if (IdleFlipMinText != null) IdleFlipMinText.Text = now.ToString("mm", CultureInfo.CurrentCulture);
            if (IdleJumpHourText != null) IdleJumpHourText.Text = now.ToString(hourFormat, CultureInfo.CurrentCulture);
            if (IdleJumpMinText != null) IdleJumpMinText.Text = now.ToString("mm", CultureInfo.CurrentCulture);
            if (ExpandedBigClock != null) ExpandedBigClock.Text = time;
            if (ExpandedMiniClock != null) ExpandedMiniClock.Text = time;

            string dateShort = now.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
            if (dateShort != _lastDateShortText)
            {
                _lastDateShortText = dateShort;
                if (IdleClockDateBadgeText != null) IdleClockDateBadgeText.Text = now.ToString("ddd d", CultureInfo.CurrentCulture);
                if (ExpandedDate != null) ExpandedDate.Text = dateShort;
                if (ExpandedBigDate != null) ExpandedBigDate.Text = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
            }
        }

        if (_machine.State == IslandState.Idle)
        {
            string clockStyle = SettingsService.NormalizeClockStyle(_settingsService?.Current?.ClockStyle);
            if (clockStyle == "Seconds")
            {
                if (IdleClockSecDigits != null) IdleClockSecDigits.Text = $":{now.Second:D2}";
            }
            else if (clockStyle == "Analog")
            {
                double secAngle = (now.Second + now.Millisecond / 1000.0) * 6.0;
                double minAngle = (now.Minute + now.Second / 60.0) * 6.0;
                double hourAngle = ((now.Hour % 12) + now.Minute / 60.0) * 30.0;
                if (AnalogSecTransform != null) AnalogSecTransform.Angle = secAngle;
                if (AnalogMinTransform != null) AnalogMinTransform.Angle = minAngle;
                if (AnalogHourTransform != null) AnalogHourTransform.Angle = hourAngle;
            }
            else if (clockStyle == "Matrix")
            {
                if (IdleClockMatrixHMin != null) IdleClockMatrixHMin.Text = time;
                if (IdleClockMatrixMeta != null) IdleClockMatrixMeta.Text = $"{now.Second:D2}s";
                if (IdleClockMatrixPulseDot != null) IdleClockMatrixPulseDot.Opacity = (now.Millisecond < 500) ? 1.0 : 0.35;
            }
            else if (clockStyle == "JumpHour")
            {
                bool use24Hour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H");
                if (IdleJumpHourText != null) IdleJumpHourText.Text = use24Hour ? now.ToString("HH", CultureInfo.CurrentCulture) : now.ToString("hh", CultureInfo.CurrentCulture);
                if (IdleJumpMinText != null) IdleJumpMinText.Text = now.ToString("mm", CultureInfo.CurrentCulture);
                if (IdleJumpSecDot != null) IdleJumpSecDot.Opacity = 0.55 + 0.45 * Math.Sin(now.TimeOfDay.TotalSeconds * Math.PI * 2.0);
            }
        }

        _countdownService?.PollCompletion();

        bool isCountdown = _countdownService is { IsActive: true } || (_countdownService?.TargetDuration > TimeSpan.Zero && !_stopwatch.IsRunning);
        bool timerRunning = (_countdownService?.IsRunning ?? false) || _stopwatch.IsRunning;
        if (MediaContainer?.MiniClockTimerActiveDot != null)
        {
            MediaContainer.MiniClockTimerActiveDot.Visibility = timerRunning ? Visibility.Visible : Visibility.Collapsed;
        }

        if (isCountdown && _countdownService != null)
        {
            string formatted = _countdownService.FormatRemaining();
            if (formatted != _lastTimerText)
            {
                _lastTimerText = formatted;
                CompactTimer.Text = formatted;
                ExpandedTimer.Text = formatted;
            }
        }
        else if (_stopwatch.IsRunning || _lastTimerText == null)
        {
            var elapsed = _stopwatch.Elapsed;
            string formattedTimer = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
            if (formattedTimer != _lastTimerText)
            {
                _lastTimerText = formattedTimer;
                CompactTimer.Text = formattedTimer;
                ExpandedTimer.Text = formattedTimer;
            }
        }

        if (_privacyService.CallStartTime.HasValue)
        {
            var callSpan = DateTimeOffset.Now - _privacyService.CallStartTime.Value;
            string callTimerText = callSpan.TotalHours >= 1 ? callSpan.ToString(@"h\:mm\:ss") : callSpan.ToString(@"mm\:ss");
            ExpandedCallTimer.Text = callTimerText;
            if (!string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp) && CompactCallText != null)
            {
                CompactCallText.Text = PrivacyIndicatorService.FormatCompactCallText(_privacyService.ActiveCallApp, callSpan);
            }
        }

        if (_machine.State == IslandState.Expanded || (_machine.State == IslandState.Compact && _settingsService?.Current?.ShowLyricsInCompact == true))
        {
            UpdateMediaTimelineUI();
        }
    }

    private string? _lastPositionText;
    private string? _lastDurationText;

    private void UpdateMediaTimelineUI()
    {
        var media = _mediaService.Current;
        if (media is not null && !string.IsNullOrWhiteSpace(media.Title))
        {
            var pos = media.GetPosition(DateTimeOffset.Now);
            string posStr = FormatTime(pos);
            if (posStr != _lastPositionText)
            {
                _lastPositionText = posStr;
                PositionText.Text = posStr;
            }

            string durStr = media.Duration > TimeSpan.Zero ? FormatTime(media.Duration) : "0:00";
            if (durStr != _lastDurationText)
            {
                _lastDurationText = durStr;
                DurationText.Text = durStr;
            }

            if (!_isUserSeeking)
            {
                double pct = media.Duration > TimeSpan.Zero
                    ? Math.Clamp((pos.TotalSeconds / media.Duration.TotalSeconds) * 100.0, 0, 100)
                    : 0;
                if (Math.Abs(TimelineSlider.Value - pct) > 0.05)
                {
                    TimelineSlider.Value = pct;
                }
            }

            _lyricsService.UpdatePosition(pos);
        }
        else
        {
            if (_lastPositionText != "0:00")
            {
                _lastPositionText = "0:00";
                PositionText.Text = "0:00";
            }
            if (_lastDurationText != "0:00")
            {
                _lastDurationText = "0:00";
                DurationText.Text = "0:00";
            }
            if (!_isUserSeeking && TimelineSlider.Value != 0)
            {
                TimelineSlider.Value = 0;
            }
            _lyricsService.Reset();
        }
    }

    private static string FormatTime(TimeSpan span)
    {
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }

    internal static void SyncObservableCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        if (source.Count == 0)
        {
            if (target.Count > 0) target.Clear();
            return;
        }

        if (source.Count == target.Count + 1 && target.Count > 0 && EqualityComparer<T>.Default.Equals(source[1], target[0]))
        {
            bool allMatch = true;
            for (int i = 1; i < target.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(source[i + 1], target[i]))
                {
                    allMatch = false;
                    break;
                }
            }
            if (allMatch)
            {
                target.Insert(0, source[0]);
                return;
            }
        }

        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!source.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (int i = 0; i < source.Count; i++)
        {
            var desired = source[i];
            if (i < target.Count)
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], desired))
                {
                    int existingIdx = target.IndexOf(desired);
                    if (existingIdx > i)
                        target.Move(existingIdx, i);
                    else
                        target.Insert(i, desired);
                }
            }
            else
            {
                target.Add(desired);
            }
        }
    }
}
