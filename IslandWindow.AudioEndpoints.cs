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
    //  Sound & Audio Output Center
    // =================================================================

    private void OnAudioEndpointsChanged(IReadOnlyList<AudioEndpointInfo> endpoints)
    {
        SyncObservableCollection(_audioEndpoints, endpoints);
        var cur = _audioEndpointService.CurrentDefault;
        if (cur != null)
        {
            AudioButtonGlyph.Text = cur.Glyph;
        }
        if (_audioContainer != null)
        {
            SyncHeroActiveDeviceUI();
        }
    }

    private void OnDefaultAudioEndpointChanged(AudioEndpointInfo newDef)
    {
        AudioButtonGlyph.Text = newDef.Glyph;
        _volumeService.ReattachDefaultEndpoint();
        _audioSpectrum.NotifyDefaultDeviceChanged();
        if (_audioContainer != null)
        {
            SyncHeroActiveDeviceUI(newDef);
            SyncMasterVolumeUI();
        }
        _machine.PushToast(new ToastContent(
            newDef.Glyph,
            "Audio Output Switched",
            newDef.Name,
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.8),
            TargetView: "audio",
            Priority: ToastPriority.System,
            Category: "audiodevice"));
    }

    internal void AudioEndpointCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AudioEndpointInfo { IsDefault: false } endpoint })
        {
            _audioEndpointService.SetDefaultEndpoint(endpoint.Id);
            _machine.PushToast(new ToastContent(
                endpoint.Glyph,
                "Audio Output",
                $"Active: {endpoint.Name}",
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "audio",
                Priority: ToastPriority.Feedback,
                Category: "audiodevice"));
        }
    }

    internal void EndpointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { Tag: string deviceId, IsMouseCaptureWithin: true })
        {
            _audioEndpointService.SetEndpointVolume(deviceId, (float)e.NewValue);
            var cur = _audioEndpointService.CurrentDefault;
            if (cur != null && string.Equals(cur.Id, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                SyncMasterVolumeUI((float)e.NewValue, _volumeService.IsMuted);
            }
        }
    }

    internal void EndpointMuteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string deviceId })
        {
            _audioEndpointService.ToggleEndpointMute(deviceId);
            e.Handled = true;
        }
    }

    internal void AudioModeOutputsBtn_Click(object sender, RoutedEventArgs e)
    {
        _isAudioAppMixerMode = false;
        SyncAudioModeSwitcherUI();
    }

    internal void AudioModeMixerBtn_Click(object sender, RoutedEventArgs e)
    {
        _isAudioAppMixerMode = true;
        _audioEndpointService.RefreshAppSessions();
        SyncAudioModeSwitcherUI();
    }

    internal void HardwareMicBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleMicMute();
    }

    internal void HeroMasterMuteBtn_Click(object sender, RoutedEventArgs e)
    {
        _volumeService.ToggleMute();
        SyncMasterVolumeUI();
    }

    internal void HeroMasterVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { IsMouseCaptureWithin: true })
        {
            _volumeService.SetVolume((float)e.NewValue);
            SyncMasterVolumeUI((float)e.NewValue, _volumeService.IsMuted);
        }
    }

    internal void AudioView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = _isAudioAppMixerMode ? AudioAppMixerScrollViewer : AudioEndpointsScrollViewer;
        if (sv != null)
        {
            SmoothScrollVertical(sv, -e.Delta * 0.85);
            e.Handled = true;
        }
    }

    private void SyncHeroActiveDeviceUI(AudioEndpointInfo? target = null)
    {
        var def = target ?? _audioEndpointService.CurrentDefault ?? _audioEndpoints.FirstOrDefault(x => x.IsDefault);
        if (def != null)
        {
            if (HeroDeviceNameText != null) HeroDeviceNameText.Text = def.Name;
            if (HeroDeviceGlyph != null) HeroDeviceGlyph.Text = def.Glyph;
            if (HeroDeviceTypeText != null) HeroDeviceTypeText.Text = $"{def.DeviceType} \u00B7 Active Output";
            if (HeroDeviceBatteryBadge != null)
            {
                HeroDeviceBatteryBadge.Visibility = def.BatteryPercent.HasValue ? Visibility.Visible : Visibility.Collapsed;
            }
            if (HeroDeviceBatteryText != null && def.BatteryPercent.HasValue)
            {
                HeroDeviceBatteryText.Text = $"{def.BatteryPercent.Value}%";
            }
        }
    }

    internal void SyncMasterVolumeUI(float? volume = null, bool? muted = null)
    {
        float vol = volume ?? _volumeService.CurrentVolume;
        bool isMuted = muted ?? _volumeService.IsMuted;
        int pct = (int)Math.Round(Math.Clamp(vol, 0f, 1f) * 100f);

        if (HeroMasterVolumeSlider != null && !HeroMasterVolumeSlider.IsMouseCaptureWithin)
        {
            HeroMasterVolumeSlider.Value = vol;
        }

        if (HeroMasterVolumePercentText != null)
        {
            HeroMasterVolumePercentText.Text = isMuted ? "0%" : $"{pct}%";
        }

        if (HeroSpeakerIcon != null)
        {
            HeroSpeakerIcon.Text = isMuted ? "\uE74F" : (vol > 0.66f ? "\uE767" : vol > 0.33f ? "\uE994" : "\uE993");
            HeroSpeakerIcon.Foreground = isMuted ? ThemeBrushes.Red : ThemeBrushes.SecondaryText;
        }

        if (HeroMasterMuteGlyph != null)
        {
            HeroMasterMuteGlyph.Text = isMuted ? "\uE74F" : "\uE767";
            HeroMasterMuteGlyph.Foreground = isMuted ? ThemeBrushes.Red : ThemeBrushes.PrimaryText;
        }
    }

    private void SyncAudioModeSwitcherUI()
    {
        if (AudioModeOutputsBtn != null && AudioModeMixerBtn != null)
        {
            if (!_isAudioAppMixerMode)
            {
                AudioModeOutputsBtn.Background = ThemeBrushes.FilterPillActiveBg;
                AudioModeOutputsBtn.Foreground = ThemeBrushes.PrimaryText;
                AudioModeOutputsBtn.BorderBrush = ThemeBrushes.DockActiveBorder;

                AudioModeMixerBtn.Background = ThemeBrushes.FilterPillInactiveBg;
                AudioModeMixerBtn.Foreground = ThemeBrushes.SecondaryText;
                AudioModeMixerBtn.BorderBrush = ThemeBrushes.FilterPillInactiveBorder;
            }
            else
            {
                AudioModeMixerBtn.Background = ThemeBrushes.FilterPillActiveBg;
                AudioModeMixerBtn.Foreground = ThemeBrushes.PrimaryText;
                AudioModeMixerBtn.BorderBrush = ThemeBrushes.DockActiveBorder;

                AudioModeOutputsBtn.Background = ThemeBrushes.FilterPillInactiveBg;
                AudioModeOutputsBtn.Foreground = ThemeBrushes.SecondaryText;
                AudioModeOutputsBtn.BorderBrush = ThemeBrushes.FilterPillInactiveBorder;
            }
        }

        if (AudioEndpointsScrollViewer != null && AudioAppMixerScrollViewer != null)
        {
            AudioEndpointsScrollViewer.Visibility = _isAudioAppMixerMode ? Visibility.Collapsed : Visibility.Visible;
            AudioAppMixerScrollViewer.Visibility = _isAudioAppMixerMode ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_isAudioAppMixerMode)
        {
            if (AudioAppMixerEmptyPlaceholder != null)
            {
                AudioAppMixerEmptyPlaceholder.Visibility = _audioAppSessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        else if (AudioAppMixerEmptyPlaceholder != null)
        {
            AudioAppMixerEmptyPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshAudioView()
    {
        SyncMasterVolumeUI();
        SyncHeroActiveDeviceUI();
        SyncAudioModeSwitcherUI();
        UpdateMicMuteUI();
        if (_isAudioAppMixerMode)
        {
            _audioEndpointService.RefreshAppSessions();
        }
    }

    private void OnAudioAppSessionsChanged(IReadOnlyList<AppAudioSessionInfo> sessions)
    {
        SyncObservableCollection(_audioAppSessions, sessions);
        if (AudioAppMixerEmptyPlaceholder != null)
        {
            AudioAppMixerEmptyPlaceholder.Visibility = (_isAudioAppMixerMode && _audioAppSessions.Count == 0)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    internal void AppSessionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { Tag: uint pid, IsMouseCaptureWithin: true })
        {
            _audioEndpointService.SetAppSessionVolume(pid, (float)e.NewValue);
        }
    }

    internal void AppSessionMuteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: uint pid })
        {
            _audioEndpointService.ToggleAppSessionMute(pid);
            _audioEndpointService.RefreshAppSessions();
        }
    }
}
