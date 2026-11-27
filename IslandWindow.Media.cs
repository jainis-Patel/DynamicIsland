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
    //  Media, Stopwatch, Pomodoro Countdown & Telemetry
    // =================================================================

    private void CommitSeek()
    {
        var media = _mediaService.Current;
        if (media is null || media.Duration <= TimeSpan.Zero) return;
        double targetSec = (TimelineSlider.Value / 100.0) * media.Duration.TotalSeconds;
        _mediaService.Seek(TimeSpan.FromSeconds(targetSec));
    }

    internal void MediaSourceBadge_Click(object sender, MouseButtonEventArgs e)
    {
        string? switchedTo = _mediaService.CycleNextSession();
        if (!string.IsNullOrEmpty(switchedTo))
        {
            _machine.PushToast(new ToastContent(
                "\uE8D6",
                "Media Source Switched",
                $"Active player: {switchedTo}",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "media",
                Priority: ToastPriority.Feedback,
                Category: "mediasource"));
        }
        e.Handled = true;
    }

    internal void FocusMediaPlayer_Click(object sender, MouseButtonEventArgs e)
    {
        if (_mediaService.FocusActivePlayerWindow())
        {
            _machine.CollapseNow();
        }
        e.Handled = true;
    }

    internal void CycleAlbumArtStyle_Click(object sender, MouseButtonEventArgs e)
    {
        CycleAlbumArtStyle();
        e.Handled = true;
    }

    internal void ClockSystemHudBadge_Click(object sender, MouseButtonEventArgs e)
    {
        SwitchExpandedView(ExpandedView.SystemHud);
        e.Handled = true;
    }

    private void OnTrackChanged()
    {
        var media = _mediaService.Current;
        if (media is null || string.IsNullOrWhiteSpace(media.Title)) return;

        _lyricsService?.LoadLyricsForTrack(media.Title, media.Artist, media.Duration);
        ExpandedArtDisc?.PulseTrackChange();

        if (_machine.State != IslandState.Expanded)
        {
            var accent = media.Accent ?? ThemeBrushes.AmberColor;
            _machine.PushToast(new ToastContent(
                "\uE8D6",
                media.Title,
                media.Artist,
                accent,
                TimeSpan.FromSeconds(2.8),
                TargetView: "media",
                Priority: ToastPriority.System,
                Category: "track"));
        }
    }

    private bool _preferClockHeaderInMedia;
    private Color? _lastAppliedMediaAccent;

    internal void SyncHomeCenterStageVisibility()
    {
        if (MediaContainer == null) return;
        var media = _mediaService?.Current;
        bool hasMedia = media is not null && !string.IsNullOrWhiteSpace(media.Title);
        bool lyricsEnabled = _settingsService?.Current?.EnableSyncedLyrics ?? true;
        bool showLyricsStage = hasMedia &&
                               !_preferClockHeaderInMedia &&
                               lyricsEnabled &&
                               (_lyricsService?.HasLyrics == true || _lyricsService?.IsLoading == true);

        if (KaraokeLyricsHost != null)
        {
            KaraokeLyricsHost.Visibility = showLyricsStage ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    internal void ToggleClockMediaHeader_Click(object sender, MouseButtonEventArgs e)
    {
        var media = _mediaService.Current;
        bool hasMedia = media is not null && !string.IsNullOrWhiteSpace(media.Title);
        if (!hasMedia) return;

        _preferClockHeaderInMedia = !_preferClockHeaderInMedia;
        ExpandedMediaHeader.Visibility = _preferClockHeaderInMedia ? Visibility.Collapsed : Visibility.Visible;
        ExpandedClockHeader.Visibility = _preferClockHeaderInMedia ? Visibility.Visible : Visibility.Collapsed;
        SyncHomeCenterStageVisibility();
        RefreshTargetGeometry();
        e.Handled = true;
    }

    private void OnMediaChanged()
    {
        var media = _mediaService.Current;
        bool hasMedia = media is not null && !string.IsNullOrWhiteSpace(media.Title);
        bool callActive = !string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp);

        if (hasMedia && media is not null)
        {
            _lyricsService?.LoadLyricsForTrack(media.Title, media.Artist, media.Duration);
        ExpandedArtDisc?.PulseTrackChange();

            if (!callActive)
            {
                ExpandedMediaHeader.Visibility = _preferClockHeaderInMedia ? Visibility.Collapsed : Visibility.Visible;
                ExpandedClockHeader.Visibility = _preferClockHeaderInMedia ? Visibility.Visible : Visibility.Collapsed;
            }
            if (MediaTransportControlsGrid != null) MediaTransportControlsGrid.Visibility = Visibility.Visible;

            ExpandedTitle.Text = media.Title;
            ExpandedArtist.Text = string.IsNullOrWhiteSpace(media.Artist) ? "Unknown Artist" : media.Artist;
            var brand = media.BrandInfo ?? MediaPlayerBrandService.Detect(media.SourceAppUserModelId, media.Title, media.Artist, media.AlbumTitle);
            string sourceLabel = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Media" : brand.DisplayName;
            ExpandedSourceApp.Text = media.ActiveSessionCount > 1
                ? $"{sourceLabel} \u00B7 1/{media.ActiveSessionCount}"
                : sourceLabel;

            if (ExpandedSourceLogoPath != null && ExpandedSourceLogoBox != null)
            {
                ExpandedSourceLogoPath.Data = brand.IconGeometry;
                ExpandedSourceLogoPath.Fill = brand.BrandBrush;
                ExpandedSourceLogoBox.Visibility = Visibility.Visible;
            }

            PlayPauseGlyph.Text = media.IsPlaying ? "\uE769" : "\uE768";
            PrevButton.IsEnabled = media.CanPrevious;
            NextButton.IsEnabled = media.CanNext;

            CompactArtDisc?.SetArtwork(media.Artwork);
            ExpandedArtDisc?.SetArtwork(media.Artwork);

            if (media.Accent.HasValue)
            {
                if (_lastAppliedMediaAccent != media.Accent.Value)
                {
                    _lastAppliedMediaAccent = media.Accent.Value;
                    Resources["AccentBrush"] = ThemeBrushes.CreateFrozen(media.Accent.Value);
                }
                PillGlowColor.Color = media.Accent.Value;
                PillGlowSecondaryColor.Color = media.SecondaryAccent ?? media.Accent.Value;
            }

            CompactTrackText.Text = media.Title;
            SyncHomeCenterStageVisibility();
        }
        else
        {
            _preferClockHeaderInMedia = false;
            if (_lastAppliedMediaAccent != null)
            {
                _lastAppliedMediaAccent = null;
                if (_settingsService != null)
                {
                    ApplyTheme(_settingsService.Current.ThemeName);
                }
            }
            CompactArtDisc?.SetArtwork(null);
            ExpandedArtDisc?.SetArtwork(null);
            _lyricsService?.Reset();
            if (!callActive)
            {
                ExpandedMediaHeader.Visibility = Visibility.Collapsed;
                ExpandedClockHeader.Visibility = Visibility.Visible;
            }
            if (MediaTransportControlsGrid != null) MediaTransportControlsGrid.Visibility = Visibility.Collapsed;
            SyncHomeCenterStageVisibility();

            PlayPauseGlyph.Text = "\uE768";
        }

        EvaluateActivity();
        UpdateMediaTimelineUI();
        EnsureRendering();
    }

    private void ToggleStopwatch()
    {
        if (_countdownService.IsActive)
        {
            _countdownService.TogglePause();
            return;
        }

        if (_stopwatch.IsRunning) _stopwatch.Stop();
        else _stopwatch.Start();
        RefreshStopwatchUI();
    }

    private static readonly int[] PomodoroPresetMinutes = [15, 25, 45, 60];
    private int _pomodoroMinutes = 25;

    internal void PomodoroButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        CyclePomodoroPreset();
        e.Handled = true;
    }

    private void CyclePomodoroPreset()
    {
        int idx = Array.IndexOf(PomodoroPresetMinutes, _pomodoroMinutes);
        _pomodoroMinutes = PomodoroPresetMinutes[(idx + 1) % PomodoroPresetMinutes.Length];
        if (PomodoroButtonText != null)
        {
            PomodoroButtonText.Text = $"{_pomodoroMinutes}m";
        }
        _machine.PushToast(new ToastContent(
            "\uE916",
            "Pomodoro Preset",
            $"Duration set to {_pomodoroMinutes} minutes",
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(1.6),
            TargetView: "media",
            Priority: ToastPriority.Feedback,
            Category: "timer"));
    }

    private void StartPomodoroTimer()
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            CyclePomodoroPreset();
            return;
        }

        _stopwatch.Reset();
        _lastTimerText = null;
        if (_countdownService.IsRunning)
        {
            _countdownService.TogglePause();
        }
        else
        {
            _countdownService.StartCountdown(TimeSpan.FromMinutes(_pomodoroMinutes), $"Pomodoro {_pomodoroMinutes}m");
            _machine.PushToast(new ToastContent(
                "\uE916",
                "Pomodoro Started",
                $"{_pomodoroMinutes}:00 focus countdown active",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.2),
                TargetView: "media",
                Priority: ToastPriority.Feedback,
                Category: "timer"));
        }
    }

    private void ResetStopwatch()
    {
        _stopwatch.Reset();
        _countdownService.Reset();
        _lastTimerText = null;
        RefreshStopwatchUI();
    }

    private void OnCountdownCompleted(string label)
    {
        _lastTimerText = null;
        RefreshStopwatchUI();
        _machine.PushToast(new ToastContent(
            "\uE916",
            $"{label} Complete!",
            "Countdown finished \u00B7 Time for a break",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(4.5),
            TargetView: "media",
            Priority: ToastPriority.Critical));
    }

    private void RefreshStopwatchUI()
    {
        bool isCountdown = _countdownService.IsActive;
        bool running = isCountdown ? _countdownService.IsRunning : _stopwatch.IsRunning;
        bool hasProgress = isCountdown ? _countdownService.Remaining < _countdownService.TargetDuration : _stopwatch.Elapsed > TimeSpan.Zero;

        ExpandedTimerModeLabel.Text = isCountdown ? _countdownService.Label : "Stopwatch";
        StartPauseGlyph.Text = running ? "\uE769" : "\uE768";
        StartPauseLabel.Text = running ? "Pause" : hasProgress ? "Resume" : "Start";

        EvaluateActivity();
        UpdateClockAndTimeline();
        EnsureRendering();
    }

    private void ShowNextDemoToast()
    {
        _machine.PushToast(DemoToasts[_demoToastIndex]);
        _demoToastIndex = (_demoToastIndex + 1) % DemoToasts.Length;
    }

    private void Pill_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_machine.State == IslandState.Expanded)
        {
            if (e.ChangedButton == MouseButton.XButton1 && TryNavigateBack())
            {
                e.Handled = true;
            }
            return;
        }
        if (e.ChangedButton != MouseButton.Middle)
            return;

        TriggerLiquidImpulse(110, -40, 14);
        if (!string.IsNullOrWhiteSpace(_privacyService.ActiveCallApp))
        {
            ToggleMicMute();
        }
        else if (_mediaService.Current != null && !string.IsNullOrWhiteSpace(_mediaService.Current.Title))
        {
            _mediaService.TogglePlayPause();
        }
        else if (_countdownService.IsActive || _stopwatch.IsRunning || _stopwatch.Elapsed > TimeSpan.Zero)
        {
            ToggleStopwatch();
        }
        e.Handled = true;
    }

    private static bool IsDescendantOfAny(DependencyObject? child, params DependencyObject?[] ancestors)
    {
        while (child != null)
        {
            for (int i = 0; i < ancestors.Length; i++)
            {
                if (ancestors[i] != null && child == ancestors[i])
                    return true;
            }
            DependencyObject? parent = null;
            if (child is Visual || child is System.Windows.Media.Media3D.Visual3D)
            {
                parent = VisualTreeHelper.GetParent(child);
            }
            child = parent ?? LogicalTreeHelper.GetParent(child);
        }
        return false;
    }

    internal void CycleClockStyle(int step)
    {
        var styles = SettingsService.AvailableClockStyles;
        if (styles == null || styles.Count == 0) return;

        string current = SettingsService.NormalizeClockStyle(_settingsService.Current.ClockStyle);
        int idx = -1;
        for (int i = 0; i < styles.Count; i++)
        {
            if (string.Equals(styles[i], current, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0) idx = 0;

        int nextIdx = (idx + step) % styles.Count;
        if (nextIdx < 0) nextIdx += styles.Count;

        string target = styles[nextIdx];
        _settingsService.Update(s => s.ClockStyle = target, nameof(AppSettings.ClockStyle));
        ApplyClockStyle(target);

        string label = target switch
        {
            "Seconds" => "Seconds (Precision)",
            "DateBadge" => "Date & Time Badge",
            "Analog" => "Micro Horology Analog",
            "Neon" => "Cyber Neon Glow",
            "Flip" => "Retro Flip Card",
            "Matrix" => "Field Telemetry Matrix",
            "JumpHour" => "Haute Jump-Hour Guichet",
            _ => "Minimal Digital"
        };
        _machine.PushToast(new ToastContent(
            "\uE823",
            "Clock Face",
            label,
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(1.6),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "clockstyle"));
    }

    private void Pill_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_machine.State == IslandState.Expanded) return;

        // Shift + Scroll skips tracks horizontally; Alt + Scroll adjusts display brightness; standard Scroll adjusts system master volume
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (e.Delta > 0) _mediaService.Next();
            else if (e.Delta < 0) _mediaService.Previous();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            int pct = _brightnessService.StepBrightness(e.Delta > 0 ? 5 : -5);
            _machine.PushToast(new ToastContent(
                "\uE706",
                "Brightness",
                $"{pct}%",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(1.8),
                TargetView: "battery",
                Priority: ToastPriority.Feedback,
                Category: "brightness"));
            e.Handled = true;
            return;
        }

        // When in Idle state: hovering over the clock face or holding Ctrl + Wheel cycles through all 8 clock styles
        if (_machine.State == IslandState.Idle)
        {
            bool isOverClock = e.OriginalSource is DependencyObject dep &&
                               IsDescendantOfAny(dep, IdleClock, IdleClockSecondsHost, IdleClockDateHost,
                                   IdleClockAnalogHost, IdleClockNeonHost, IdleClockFlipHost,
                                   IdleClockMatrixHost, IdleClockJumpHourHost);

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 || isOverClock)
            {
                CycleClockStyle(e.Delta < 0 ? 1 : -1);
                e.Handled = true;
                return;
            }
        }

        // Fluid downward pull-to-expand gesture via trackpad scroll
        var pullRes = _touchpadProcessor.ProcessVerticalScroll(e.Delta, Environment.TickCount64, _machine.State);
        if (pullRes.Kind == GestureActionKind.ExpandPull)
        {
            ExecuteGestureResult(pullRes);
            e.Handled = true;
            return;
        }

        float delta = e.Delta > 0 ? 0.02f : -0.02f;
        _volumeService.StepVolume(delta);
        e.Handled = true;
    }

    private void OnVolumeChanged(float vol, bool isMuted)
    {
        SyncMasterVolumeUI(vol, isMuted);
        if (_machine.State == IslandState.Expanded) return;

        string glyph = isMuted ? "\uE74F" : (vol > 0.66f ? "\uE767" : vol > 0.33f ? "\uE994" : "\uE993");
        string title = isMuted ? "Volume Muted" : "Volume";
        string subtitle = isMuted ? "0%" : $"{Math.Round(vol * 100)}%";
        Color accent = isMuted ? ThemeBrushes.RedColor : ThemeBrushes.BlueColor;

        _machine.PushToast(new ToastContent(
            glyph,
            title,
            subtitle,
            accent,
            TimeSpan.FromSeconds(1.8),
            TargetView: "audio",
            Priority: ToastPriority.Feedback,
            Category: "volume"));
    }

}
