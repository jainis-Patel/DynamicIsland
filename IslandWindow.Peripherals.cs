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
    //  Phone Link & Peripheral Wireless Battery Hub
    // =================================================================

    private bool _peripheralsConnectedOnly;

    private void OnPeripheralsChanged(IReadOnlyList<PeripheralDeviceInfo> devices)
    {
        var filtered = _peripheralsConnectedOnly
            ? devices.Where(d => d.IsConnected).ToList()
            : devices;
        SyncObservableCollection(_peripheralDevices, filtered);
        PeripheralsEmptyPlaceholder.Visibility = filtered.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        PeripheralsButtonGlyph.Foreground = _peripheralService.HasLowBatteryDevice
            ? ThemeBrushes.Red
            : ThemeBrushes.PrimaryText;
    }

    internal void PeripheralsFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        _peripheralsConnectedOnly = !_peripheralsConnectedOnly;
        if (PeripheralsFilterText != null)
        {
            PeripheralsFilterText.Text = _peripheralsConnectedOnly ? "Connected Only" : "All Paired";
            PeripheralsFilterText.Foreground = _peripheralsConnectedOnly ? ThemeBrushes.Green : ThemeBrushes.SecondaryText;
        }
        if (PeripheralsFilterBtn != null)
        {
            PeripheralsFilterBtn.BorderBrush = _peripheralsConnectedOnly
                ? ThemeBrushes.CreateFrozen(Color.FromArgb(0x45, 0x30, 0xD1, 0x58))
                : Brushes.Transparent;
        }
        OnPeripheralsChanged(_peripheralService.Devices);
    }

    private void OnPeripheralLowBatteryAlert(PeripheralDeviceInfo dev)
    {
        _machine.PushToast(new ToastContent(
            "\uE851",
            dev.Name,
            $"Battery low \u00B7 {dev.BatteryPercent}% remaining",
            ThemeBrushes.RedColor,
            TimeSpan.FromSeconds(4.5),
            TargetView: "peripherals",
            Priority: ToastPriority.Critical));
    }

    internal async void RefreshPeripheralsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _peripheralService.RefreshAsync();
            if (_isClosing) return;
            _machine.PushToast(new ToastContent(
                "\uE72C",
                "Peripherals Refreshed",
                $"{_peripheralService.ConnectedCount} connected \u00B7 {_peripheralService.Devices.Count} paired",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "peripherals",
                Priority: ToastPriority.Feedback,
                Category: "peripherals"));
        }
        catch (Exception ex)
        {
            App.Log($"[Peripherals] Refresh failed: {ex.Message}");
        }
    }

    internal void OpenBluetoothSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        PeripheralDeviceService.OpenBluetoothSettings();
    }

    internal async void PeripheralCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PeripheralDeviceInfo dev }) return;
        e.Handled = true;

        try
        {
            // Check if this Bluetooth device matches an audio output endpoint
            var audioMatch = _audioEndpointService.Endpoints.FirstOrDefault(ep =>
                ep.Name.Contains(dev.Name, StringComparison.OrdinalIgnoreCase) ||
                dev.Name.Contains(ep.Name, StringComparison.OrdinalIgnoreCase));

            if (audioMatch != null && !audioMatch.IsDefault)
            {
                _audioEndpointService.SetDefaultEndpoint(audioMatch.Id);
                _machine.PushToast(new ToastContent(
                    dev.Glyph,
                    "Audio Routed",
                    $"Switched output to {dev.Name}",
                    ThemeBrushes.GreenColor,
                    TimeSpan.FromSeconds(2.2),
                    TargetView: "peripherals",
                    Priority: ToastPriority.Feedback,
                    Category: "audiodevice"));
                return;
            }

            _machine.PushToast(new ToastContent(
                dev.Glyph,
                dev.IsConnected ? dev.Name : $"Connecting {dev.Name}...",
                dev.IsConnected ? dev.BatteryStatusText : "Waking paired Bluetooth peripheral",
                ThemeBrushes.BlueColor,
                TimeSpan.FromSeconds(2.2),
                TargetView: "peripherals",
                Priority: ToastPriority.Feedback,
                Category: "peripherals"));

            await _peripheralService.ConnectDeviceAsync(dev);
            if (_isClosing) return;
        }
        catch (Exception ex)
        {
            App.Log($"[Peripherals] PeripheralCard failed: {ex.Message}");
        }
    }

}
