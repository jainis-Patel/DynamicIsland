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
    //  Audio Output & Bluetooth Quick-Switcher
    // =================================================================

    private void OnAudioEndpointsChanged(IReadOnlyList<AudioEndpointInfo> endpoints)
    {
        SyncObservableCollection(_audioEndpoints, endpoints);
        var cur = _audioEndpointService.CurrentDefault;
        if (cur != null)
        {
            AudioButtonGlyph.Text = cur.Glyph;
        }
    }

    private void OnDefaultAudioEndpointChanged(AudioEndpointInfo newDef)
    {
        AudioButtonGlyph.Text = newDef.Glyph;
        _volumeService.ReattachDefaultEndpoint();
        _audioSpectrum.NotifyDefaultDeviceChanged();
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

    internal void AudioModeToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _isAudioAppMixerMode = !_isAudioAppMixerMode;
        if (AudioSectionTitle != null)
        {
            AudioSectionTitle.Text = _isAudioAppMixerMode ? "Per-App Volume Mixer" : "Audio Output";
        }
        if (AudioModeToggleText != null)
        {
            AudioModeToggleText.Text = _isAudioAppMixerMode ? "Output Devices" : "App Mixer";
            AudioModeToggleText.Foreground = _isAudioAppMixerMode ? ThemeBrushes.Green : ThemeBrushes.Blue;
        }
        if (AudioEndpointsScrollViewer != null && AudioAppMixerScrollViewer != null)
        {
            AudioEndpointsScrollViewer.Visibility = _isAudioAppMixerMode ? Visibility.Collapsed : Visibility.Visible;
            AudioAppMixerScrollViewer.Visibility = _isAudioAppMixerMode ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_isAudioAppMixerMode)
        {
            _audioEndpointService.RefreshAppSessions();
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
