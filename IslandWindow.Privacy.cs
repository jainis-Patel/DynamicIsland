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
    //  Privacy Dots, Call Indicators & Microphone Mute Controls
    // =================================================================

    private string? _lastAnnouncedCallApp;

    private void OnPrivacyStateChanged()
    {
        bool cam = _privacyService.IsCameraActive;
        bool mic = _privacyService.IsMicrophoneActive;
        string? callApp = _privacyService.ActiveCallApp;

        CamPrivacyDot.Visibility = cam ? Visibility.Visible : Visibility.Collapsed;
        MicPrivacyDot.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
        bool showCam = _settingsService?.Current?.ShowCameraLensIcon ?? true;
        IdleSensorDot.Visibility = (!cam && !mic && showCam) ? Visibility.Visible : Visibility.Collapsed;

        if (NotchPrivacyLed != null)
        {
            bool hardwareNotch = _settingsService?.Current.HardwareNotchMode ?? false;
            if (hardwareNotch && (cam || mic))
            {
                NotchPrivacyLed.Visibility = Visibility.Visible;
                NotchPrivacyLed.Fill = cam ? ThemeBrushes.Green : ThemeBrushes.Amber;
            }
            else
            {
                NotchPrivacyLed.Visibility = Visibility.Collapsed;
            }
        }

        if (!string.IsNullOrWhiteSpace(callApp))
        {
            var callSpan = _privacyService.CallStartTime.HasValue
                ? DateTimeOffset.Now - _privacyService.CallStartTime.Value
                : TimeSpan.Zero;
            CompactCallText.Text = PrivacyIndicatorService.FormatCompactCallText(callApp, callSpan);
            ExpandedCallHeader.Visibility = Visibility.Visible;
            ExpandedMediaHeader.Visibility = Visibility.Collapsed;
            ExpandedClockHeader.Visibility = Visibility.Collapsed;

            ExpandedCallTitle.Text = $"{callApp} Call";
            ExpandedCallStatus.Text = cam ? "Video & Voice Session" : "Voice Call Session";
            ExpandedCallMicBadge.Visibility = Visibility.Visible;
            ExpandedCallCamBadge.Visibility = cam ? Visibility.Visible : Visibility.Collapsed;

            if (!string.Equals(_lastAnnouncedCallApp, callApp, StringComparison.OrdinalIgnoreCase))
            {
                _lastAnnouncedCallApp = callApp;
                _machine.PushToast(new ToastContent(
                    "\uE717",
                    $"{callApp} Call Active",
                    cam ? "Camera & microphone active \u00B7 Click for call controls" : "Microphone active \u00B7 Click for call controls",
                    ThemeBrushes.GreenColor,
                    TimeSpan.FromSeconds(3.0),
                    TargetView: "media",
                    Priority: ToastPriority.System,
                    Category: "call"));
            }
        }
        else
        {
            _lastAnnouncedCallApp = null;
            ExpandedCallHeader.Visibility = Visibility.Collapsed;
            bool hasMedia = _mediaService.Current is not null && !string.IsNullOrWhiteSpace(_mediaService.Current.Title);
            ExpandedMediaHeader.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
            ExpandedClockHeader.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;
        }

        UpdateMicMuteUI();
        EvaluateActivity();
        EnsureRendering();
    }

    private void UpdateMicMuteUI()
    {
        bool muted = _privacyService.IsMicrophoneMuted;
        MicMuteGlyph.Text = muted ? "\uE74F" : "\uE720";
        MicMuteButton.Background = muted ? ThemeBrushes.MicMutedBg : ThemeBrushes.ButtonSubtleBg;
        MicMuteGlyph.Foreground = muted ? ThemeBrushes.Red : ThemeBrushes.PrimaryText;
        MicMuteButton.ToolTip = muted ? "Unmute Microphone" : "Mute Microphone";

        if (ExpandedCallMicBadge != null && ExpandedCallMicText != null)
        {
            ExpandedCallMicText.Text = muted ? "Mic Muted \u00B7 Unmute" : "Mic Active \u00B7 Mute";
            ExpandedCallMicText.Foreground = muted ? ThemeBrushes.Red : ThemeBrushes.Green;
            ExpandedCallMicBadge.Background = muted
                ? ThemeBrushes.AlertRedBg
                : ThemeBrushes.PlanSaverActiveBg;
            ExpandedCallMicBadge.BorderBrush = muted
                ? ThemeBrushes.AlertRedBorder
                : ThemeBrushes.PlanSaverActiveBorder;
        }

        if (CompactCallBadge != null && CompactCallGlyph != null && CompactCallText != null)
        {
            CompactCallGlyph.Text = muted ? "\uE74F" : "\uE717";
            CompactCallGlyph.Foreground = muted ? ThemeBrushes.Red : ThemeBrushes.Green;
            CompactCallBadge.Background = muted
                ? ThemeBrushes.AlertRedBg
                : ThemeBrushes.PlanSaverActiveBg;
            CompactCallText.Foreground = muted ? ThemeBrushes.Red : ThemeBrushes.Green;
        }

        if (CompactMicToggleBtn != null && CompactMicToggleGlyph != null)
        {
            CompactMicToggleGlyph.Text = muted ? "\uE74F" : "\uE720";
            CompactMicToggleGlyph.Foreground = muted ? ThemeBrushes.Red : ThemeBrushes.Green;
            CompactMicToggleBtn.Background = muted ? ThemeBrushes.AlertRedBg : ThemeBrushes.PlanSaverActiveBg;
            CompactMicToggleBtn.BorderBrush = muted ? ThemeBrushes.AlertRedBorder : ThemeBrushes.PlanSaverActiveBorder;
            CompactMicToggleBtn.ToolTip = muted ? "Microphone Muted \u00B7 Click to unmute (Ctrl+Alt+K)" : "Microphone Active \u00B7 Click to mute (Ctrl+Alt+K)";
        }

        if (HardwareMicGlyph != null && HardwareMicText != null)
        {
            HardwareMicGlyph.Text = muted ? "\uE74F" : "\uE720";
            HardwareMicGlyph.Foreground = muted ? ThemeBrushes.Red : ThemeBrushes.Green;
            HardwareMicText.Text = muted ? "Mic: Muted" : "Mic: Active";
        }
    }

    internal void CompactMicToggleBtn_Click(object sender, MouseButtonEventArgs e)
    {
        ToggleMicMute();
        e.Handled = true;
    }

    internal void CompactCallBadge_Click(object sender, MouseButtonEventArgs e)
    {
        if (PrivacyIndicatorService.FocusCallAppWindow(_privacyService.ActiveCallApp))
        {
            _machine.CollapseNow();
        }
        e.Handled = true;
    }

    internal void ExpandedCallMicBadge_Click(object sender, MouseButtonEventArgs e)
    {
        ToggleMicMute();
        e.Handled = true;
    }

    internal void ExpandedCallTitle_Click(object sender, MouseButtonEventArgs e)
    {
        if (PrivacyIndicatorService.FocusCallAppWindow(_privacyService.ActiveCallApp))
        {
            _machine.CollapseNow();
        }
        e.Handled = true;
    }

    private void ToggleMicMute()
    {
        bool muted = _privacyService.ToggleMicrophoneMute();
        UpdateMicMuteUI();

        string glyph = muted ? "\uE74F" : "\uE720";
        string title = muted ? "Microphone Muted" : "Microphone Unmuted";
        string sub = muted ? "Hardware capture disabled" : "Hardware capture active";
        Color accent = muted ? ThemeBrushes.RedColor : ThemeBrushes.GreenColor;

        _machine.PushToast(new ToastContent(
            glyph,
            title,
            sub,
            accent,
            TimeSpan.FromSeconds(2.0),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "mic"));
    }

}
