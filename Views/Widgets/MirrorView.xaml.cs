using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class MirrorView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;
    private WebcamMirrorService? _service;
    private bool _ringLightOn;
    private bool _isMirrored = true;
    private bool _subscribed;

    public MirrorView()
    {
        InitializeComponent();
        Unloaded += (_, _) => SetActive(false);
    }

    public void SetActive(bool active)
    {
        var svc = Island?.WebcamMirror;
        if (svc == null) return;
        _service = svc;

        if (active)
        {
            if (!_subscribed)
            {
                svc.FrameArrived += OnFrameArrived;
                svc.MicLevelChanged += OnMicLevelChanged;
                svc.StatusChanged += OnStatusChanged;
                _subscribed = true;
            }
            StatusOverlay.Visibility = Visibility.Visible;
            StatusText.Text = "Starting camera...";
            svc.StartAsync().FireAndForget("MirrorView.Start");
            RefreshMicMuteLabel();
        }
        else
        {
            if (_subscribed)
            {
                svc.FrameArrived -= OnFrameArrived;
                svc.MicLevelChanged -= OnMicLevelChanged;
                svc.StatusChanged -= OnStatusChanged;
                _subscribed = false;
            }
            CameraPreviewImage.Source = null;
            StatusOverlay.Visibility = Visibility.Visible;
            StatusText.Text = "Camera paused";
            svc.StopAsync().FireAndForget("MirrorView.Stop");
        }
    }

    private void OnFrameArrived(WriteableBitmap bmp)
    {
        if (!ReferenceEquals(CameraPreviewImage.Source, bmp))
        {
            CameraPreviewImage.Source = bmp;
        }
        if (StatusOverlay.Visibility != Visibility.Collapsed)
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
        }
        if (_service != null)
        {
            CameraNameText.Text = _service.ActiveCameraName;
            SwitchCameraBtn.IsEnabled = _service.CameraCount > 1;
        }
    }

    private void OnMicLevelChanged(float level)
    {
        double targetW = Math.Clamp(level * 132.0, 2.0, 132.0);
        MicLevelFill.Width = targetW;
        MicLevelFill.Background = level >= 0.85f
            ? ThemeBrushes.Red
            : (level >= 0.65f ? ThemeBrushes.Amber : ThemeBrushes.Green);
    }

    private void OnStatusChanged(string status)
    {
        if (string.IsNullOrEmpty(status))
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
            if (_service != null)
            {
                CameraNameText.Text = _service.ActiveCameraName;
                SwitchCameraBtn.IsEnabled = _service.CameraCount > 1;
            }
        }
        else
        {
            StatusOverlay.Visibility = Visibility.Visible;
            StatusText.Text = status;
        }
    }

    private void RingLightBtn_Click(object sender, RoutedEventArgs e)
    {
        _ringLightOn = !_ringLightOn;
        RingLightOverlay.Visibility = _ringLightOn ? Visibility.Visible : Visibility.Collapsed;
        RingLightBtnText.Text = _ringLightOn ? "Ring Light: On" : "Ring Light: Off";
        Island?.TriggerLiquidImpulse(24, -8, 4);
    }

    private void FlipMirrorBtn_Click(object sender, RoutedEventArgs e)
    {
        _isMirrored = !_isMirrored;
        MirrorFlipTransform.ScaleX = _isMirrored ? -1 : 1;
        FlipMirrorBtnText.Text = _isMirrored ? "Mirror: Flipped" : "Mirror: Normal";
    }

    private void SwitchCameraBtn_Click(object sender, RoutedEventArgs e)
    {
        _service?.SwitchCameraAsync().FireAndForget("MirrorView.SwitchCamera");
    }

    private void MirrorMicMuteBtn_Click(object sender, RoutedEventArgs e)
    {
        Island?.ToggleHardwareMicMute();
        RefreshMicMuteLabel();
    }

    private void RefreshMicMuteLabel()
    {
        bool muted = Island?.IsHardwareMicMuted ?? false;
        MirrorMicMuteText.Text = muted ? "Unmute" : "Mute";
        MirrorMicMuteText.Foreground = muted ? ThemeBrushes.Red : ThemeBrushes.PrimaryText;
    }
}
