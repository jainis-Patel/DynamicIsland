using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace DynamicIsland.Services;

/// <summary>
/// Watches paired Bluetooth Classic + LE devices and raises an alert when one connects or disconnects.
/// </summary>
internal sealed class BluetoothMonitor : IDisposable
{
    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(4);
    private static readonly Color Blue = Color.FromRgb(0x0A, 0x84, 0xFF);
    private static readonly Color Grey = Color.FromRgb(0xAE, 0xAE, 0xB2);
    private static readonly string[] AudioHints = { "airpods", "buds", "headphone", "headset", "earbud", "wh-", "wf-", "beats", "bose", "jbl" };

    private readonly Dispatcher _ui;
    private readonly List<DeviceWatcher> _watchers = new();
    private readonly Dictionary<string, (string Name, bool Connected)> _devices = new();
    private readonly Dictionary<string, DateTime> _recentAlerts = new();

    public event Action<ToastContent>? Alert;

    public BluetoothMonitor(Dispatcher ui) => _ui = ui;

    public void Start()
    {
        string[] selectors =
        {
            BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
        };

        foreach (string selector in selectors)
        {
            try
            {
                var watcher = DeviceInformation.CreateWatcher(selector, new[] { IsConnectedKey },
                    DeviceInformationKind.AssociationEndpoint);
                watcher.Added += (_, info) => _ui.InvokeAsync(() =>
                {
                    try { _devices[info.Id] = (info.Name, ReadConnected(info.Properties)); }
                    catch { }
                });
                watcher.Updated += (_, update) => _ui.InvokeAsync(() =>
                {
                    try { OnUpdated(update); }
                    catch { }
                });
                watcher.Removed += (_, update) => _ui.InvokeAsync(() =>
                {
                    try { _devices.Remove(update.Id); }
                    catch { }
                });
                watcher.Start();
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                App.Log($"[Bluetooth] Watcher creation failed: {ex.Message}");
            }
        }
    }

    private void OnUpdated(DeviceInformationUpdate update)
    {
        try
        {
            if (!update.Properties.ContainsKey(IsConnectedKey) || update.Properties[IsConnectedKey] is not bool connected) return;
            if (!_devices.TryGetValue(update.Id, out var device) || device.Connected == connected) return;
            _devices[update.Id] = (device.Name, connected);

            string key = $"{device.Name}|{connected}";
            var now = DateTime.UtcNow;
            if (_recentAlerts.TryGetValue(key, out var last) && now - last < DuplicateWindow) return;
            _recentAlerts[key] = now;

            string name = string.IsNullOrWhiteSpace(device.Name) ? "Bluetooth device" : device.Name;
            bool isAudio = AudioHints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase));
            string glyph = isAudio ? "\uE7F6" : "\uE702";

            Alert?.Invoke(connected
                ? new ToastContent(glyph, name, "Connected", Blue, TimeSpan.FromSeconds(3))
                : new ToastContent(glyph, name, "Disconnected", Grey, TimeSpan.FromSeconds(2.5)));
        }
        catch (Exception ex)
        {
            App.Log($"[Bluetooth] OnUpdated exception: {ex.Message}");
        }
    }

    private static bool ReadConnected(IReadOnlyDictionary<string, object> properties)
    {
        try
        {
            return properties.ContainsKey(IsConnectedKey) && properties[IsConnectedKey] is true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    watcher.Stop();
            }
            catch { }
        }
        _watchers.Clear();
    }
}
