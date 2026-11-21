using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

public sealed class PeripheralDeviceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Peripheral"; // Mouse, Keyboard, Controller, Headset, Phone, Stylus
    public string Glyph { get; set; } = "\uE702";
    public int? BatteryPercent { get; set; }
    public bool IsCharging { get; set; }
    public bool IsConnected { get; set; }
    public bool IsSimulated { get; set; }

    public bool IsLowBattery => IsConnected && BatteryPercent.HasValue && BatteryPercent.Value <= 20;

    public string BatteryStatusText
    {
        get
        {
            if (!IsConnected)
                return "Paired · Click to connect";
            if (!BatteryPercent.HasValue)
                return "Connected";
            if (IsCharging)
                return $"Charging · {BatteryPercent}%";
            return $"{BatteryPercent}% Remaining";
        }
    }

    public string RightHeaderText => IsConnected
        ? (BatteryPercent.HasValue ? $"{BatteryPercent}%" : "Active")
        : "Connect";

    public double CardOpacity => IsConnected ? 1.0 : 0.80;

    public Brush BatteryBrush
    {
        get
        {
            if (!IsConnected) return ThemeBrushes.Blue;
            if (!BatteryPercent.HasValue) return ThemeBrushes.Green;
            if (BatteryPercent.Value <= 20) return ThemeBrushes.Red;
            if (BatteryPercent.Value <= 45) return ThemeBrushes.Amber;
            return ThemeBrushes.Green;
        }
    }

    public double BatteryFillWidth => IsConnected
        ? (BatteryPercent.HasValue ? Math.Clamp((BatteryPercent.Value / 100.0) * 44.0, 4.0, 44.0) : 44.0)
        : 0;
}

/// <summary>
/// Monitors connected wireless Bluetooth peripherals, HID accessories, game controllers, and paired phones.
/// Queries real-time battery gauges, exposes peripheral hub card data, and fires low-battery warning alerts (&lt; 20%).
/// </summary>
internal sealed class PeripheralDeviceService : IDisposable
{
    private const string PKEY_BatteryLevel = "{104ea319-6ee2-4701-bd47-8ddbf425bbe5} 2";

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _pollTimer;
    private readonly HashSet<string> _alertedLowBatteryIds = new(StringComparer.OrdinalIgnoreCase);
    private List<PeripheralDeviceInfo> _devices = new();
    private bool _useSimulatedFallback;
    private int _refreshing;
    private bool _xinputUnavailable;
    private bool _hasXInputControllers;
    private bool _isExpanded;
    private bool _isSuspended;
    private bool _disposed;

    public IReadOnlyList<PeripheralDeviceInfo> Devices => _devices;
    public int ConnectedCount => _devices.Count(d => d.IsConnected);
    public bool HasLowBatteryDevice => _devices.Any(d => d.IsConnected && d.IsLowBattery);

    public event Action<IReadOnlyList<PeripheralDeviceInfo>>? DevicesChanged;
    public event Action<PeripheralDeviceInfo>? LowBatteryAlert;

    public PeripheralDeviceService(Dispatcher ui)
    {
        _ui = ui;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _pollTimer.Tick += OnPollTick;
    }

    public void Start()
    {
        UpdatePollTimerState();
        _ = RefreshAsync();
    }

    public void Stop()
    {
        _pollTimer.Stop();
    }

    public void SetExpanded(bool expanded)
    {
        if (_disposed || _isExpanded == expanded) return;
        _isExpanded = expanded;
        UpdatePollTimerState();
        if (expanded && !_isSuspended)
        {
            _ = RefreshAsync();
        }
    }

    public void SetSuspended(bool suspended)
    {
        if (_disposed) return;
        _isSuspended = suspended;
        UpdatePollTimerState();
    }

    private void UpdatePollTimerState()
    {
        if (_disposed || _isSuspended)
        {
            _pollTimer.Stop();
            return;
        }

        if (_isExpanded)
        {
            _pollTimer.Interval = TimeSpan.FromSeconds(30);
            if (!_pollTimer.IsEnabled) _pollTimer.Start();
        }
        else if (_hasXInputControllers)
        {
            _pollTimer.Interval = TimeSpan.FromMinutes(1);
            if (!_pollTimer.IsEnabled) _pollTimer.Start();
        }
        else
        {
            _pollTimer.Stop();
        }
    }

    public void ToggleSimulatedFallback()
    {
        _useSimulatedFallback = !_useSimulatedFallback;
        _ = RefreshAsync();
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed || Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
            return;

        try
        {
            var (list, hasXInput) = await Task.Run(async () =>
            {
                var found = new List<PeripheralDeviceInfo>();
                bool anyController = false;
                try
                {
                    if (!_xinputUnavailable)
                    {
                        QueryGameControllers(found);
                        anyController = found.Count > 0;
                    }

                    await QueryBluetoothAndHidDevicesAsync(found).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    App.Log($"[PeripheralService] Query error: {ex.Message}");
                }

                if (found.Count == 0 && _useSimulatedFallback)
                {
                    found.AddRange(GetSamplePeripherals());
                }

                return (found, anyController);
            }).ConfigureAwait(false);

            if (_disposed) return;

            await _ui.InvokeAsync(() =>
            {
                if (_disposed) return;
                _hasXInputControllers = hasXInput;
                UpdatePollTimerState();
                _devices = list;
                DevicesChanged?.Invoke(_devices);

                foreach (var dev in _devices)
                {
                    if (dev.IsConnected && dev.IsLowBattery)
                    {
                        if (_alertedLowBatteryIds.Add(dev.Id))
                        {
                            LowBatteryAlert?.Invoke(dev);
                        }
                    }
                    else if (dev.BatteryPercent.HasValue && dev.BatteryPercent.Value > 25)
                    {
                        _alertedLowBatteryIds.Remove(dev.Id);
                    }
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void QueryGameControllers(List<PeripheralDeviceInfo> list)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                var batt = new XINPUT_BATTERY_INFORMATION();
                int ret;
                try
                {
                    ret = XInputGetBatteryInformation14(i, BATTERY_DEVTYPE_GAMEPAD, ref batt);
                }
                catch
                {
                    try
                    {
                        ret = XInputGetBatteryInformation13(i, BATTERY_DEVTYPE_GAMEPAD, ref batt);
                    }
                    catch
                    {
                        _xinputUnavailable = true;
                        break;
                    }
                }

                if (ret == 0 && batt.BatteryType != BATTERY_TYPE_DISCONNECTED)
                {
                    int pct;
                    bool isCharging = false;
                    switch (batt.BatteryType)
                    {
                        case BATTERY_TYPE_WIRED:
                            pct = 100;
                            isCharging = true;
                            break;
                        default:
                            pct = batt.BatteryLevel switch
                            {
                                BATTERY_LEVEL_EMPTY => 10,
                                BATTERY_LEVEL_LOW => 25,
                                BATTERY_LEVEL_MEDIUM => 60,
                                BATTERY_LEVEL_FULL => 95,
                                _ => 80
                            };
                            break;
                    }

                    list.Add(new PeripheralDeviceInfo
                    {
                        Id = $"controller_{i}",
                        Name = i == 0 ? "Xbox Wireless Controller" : $"Xbox Controller #{i + 1}",
                        Category = "Controller",
                        Glyph = "\uE7FC",
                        BatteryPercent = pct,
                        IsCharging = isCharging,
                        IsConnected = true
                    });
                }
            }
            catch { }
        }
    }

    private static async Task QueryBluetoothAndHidDevicesAsync(List<PeripheralDeviceInfo> list)
    {
        try
        {
            // Query both Classic Bluetooth and BLE paired devices
            const string aqs = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd35-4697-bb81-2c022f7b8842}\" OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\") AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
            string[] properties =
            {
                "System.ItemNameDisplay",
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.Category",
                PKEY_BatteryLevel
            };

            var collection = await DeviceInformation.FindAllAsync(aqs, properties);
            var byName = new Dictionary<string, PeripheralDeviceInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var dev in collection)
            {
                string name = dev.Name?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                bool isConnected = dev.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connObj) && connObj is bool b && b;

                int? battery = null;
                if (dev.Properties.TryGetValue(PKEY_BatteryLevel, out var battObj) && battObj != null)
                {
                    if (battObj is byte bVal) battery = bVal;
                    else if (battObj is int iVal) battery = iVal;
                }

                var (category, glyph) = CategorizeDevice(name);
                var candidate = new PeripheralDeviceInfo
                {
                    Id = dev.Id,
                    Name = name,
                    Category = category,
                    Glyph = glyph,
                    BatteryPercent = battery,
                    IsConnected = isConnected,
                    IsCharging = false
                };

                if (byName.TryGetValue(name, out var existing))
                {
                    if ((candidate.IsConnected && !existing.IsConnected) ||
                        (candidate.BatteryPercent.HasValue && !existing.BatteryPercent.HasValue))
                    {
                        byName[name] = candidate;
                    }
                }
                else
                {
                    byName[name] = candidate;
                }
            }

            foreach (var item in byName.Values
                .OrderByDescending(d => d.IsConnected)
                .ThenByDescending(d => d.Category == "Headset")
                .ThenBy(d => d.Name))
            {
                list.Add(item);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PeripheralService] WinRT Device query: {ex.Message}");
        }
    }

    /// <summary>
    /// Initiates a WinRT Bluetooth connection wake-up for a paired Classic BT or BLE peripheral.
    /// </summary>
    public async Task<bool> ConnectDeviceAsync(PeripheralDeviceInfo dev)
    {
        if (dev == null || string.IsNullOrWhiteSpace(dev.Id)) return false;

        bool triggered = await Task.Run(async () =>
        {
            try
            {
                using var btDevice = await BluetoothDevice.FromIdAsync(dev.Id);
                if (btDevice != null)
                {
                    var rfcomm = await btDevice.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
                    _ = rfcomm?.Services?.Count;
                    return true;
                }
            }
            catch { }

            try
            {
                using var bleDevice = await BluetoothLEDevice.FromIdAsync(dev.Id);
                if (bleDevice != null)
                {
                    var gatt = await bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    _ = gatt?.Services?.Count;
                    return true;
                }
            }
            catch { }

            return false;
        }).ConfigureAwait(false);

        await Task.Delay(700).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        return triggered;
    }

    public static void OpenBluetoothSettings()
    {
        SafeProcessLauncher.TryOpenShellUri("ms-settings:bluetooth");
    }

    private static (string category, string glyph) CategorizeDevice(string name) =>
        DeviceClassifier.ClassifyPeripheral(name);

    private static IEnumerable<PeripheralDeviceInfo> GetSamplePeripherals()
    {
        return new[]
        {
            new PeripheralDeviceInfo
            {
                Id = "sample_mouse",
                Name = "Logitech MX Master 3S",
                Category = "Mouse",
                Glyph = "\uE962",
                BatteryPercent = 85,
                IsConnected = true,
                IsSimulated = true
            },
            new PeripheralDeviceInfo
            {
                Id = "sample_keyboard",
                Name = "Apple Magic Keyboard",
                Category = "Keyboard",
                Glyph = "\uE92E",
                BatteryPercent = 68,
                IsConnected = true,
                IsSimulated = true
            },
            new PeripheralDeviceInfo
            {
                Id = "sample_phone",
                Name = "Pixel 8 Pro (Phone Link)",
                Category = "Phone",
                Glyph = "\uE8EA",
                BatteryPercent = 74,
                IsConnected = true,
                IsCharging = true,
                IsSimulated = true
            },
            new PeripheralDeviceInfo
            {
                Id = "sample_controller",
                Name = "Xbox Wireless Controller",
                Category = "Controller",
                Glyph = "\uE7FC",
                BatteryPercent = 18,
                IsConnected = true,
                IsSimulated = true
            }
        };
    }

    public void Dispose()
    {
        _disposed = true;
        _pollTimer.Stop();
    }
}
