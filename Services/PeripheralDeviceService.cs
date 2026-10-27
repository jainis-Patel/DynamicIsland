using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
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

    public bool IsLowBattery => BatteryPercent.HasValue && BatteryPercent.Value <= 20;

    public string BatteryStatusText
    {
        get
        {
            if (!BatteryPercent.HasValue)
                return IsConnected ? "Connected" : "Disconnected";
            if (IsCharging)
                return $"Charging · {BatteryPercent}%";
            return $"{BatteryPercent}% Remaining";
        }
    }

    public Brush BatteryBrush
    {
        get
        {
            if (!BatteryPercent.HasValue) return ThemeBrushes.SecondaryText;
            if (BatteryPercent.Value <= 20) return ThemeBrushes.Red;
            if (BatteryPercent.Value <= 45) return ThemeBrushes.Amber;
            return ThemeBrushes.Green;
        }
    }

    public double BatteryFillWidth => BatteryPercent.HasValue ? Math.Clamp((BatteryPercent.Value / 100.0) * 44.0, 4.0, 44.0) : 0;
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
            Interval = TimeSpan.FromMinutes(3)
        };
        _pollTimer.Tick += OnPollTick;
    }

    public void Start()
    {
        _pollTimer.Start();
        _ = RefreshAsync();
    }

    public void Stop()
    {
        _pollTimer.Stop();
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
            var list = await Task.Run(async () =>
            {
                var found = new List<PeripheralDeviceInfo>();
                try
                {
                    if (!_xinputUnavailable)
                        QueryGameControllers(found);

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

                return found;
            }).ConfigureAwait(false);

            if (_disposed) return;

            await _ui.InvokeAsync(() =>
            {
                if (_disposed) return;
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
            const string aqs = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd35-4697-bb81-2c022f7b8842}\"";
            string[] properties =
            {
                "System.ItemNameDisplay",
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.Category",
                PKEY_BatteryLevel
            };

            var collection = await DeviceInformation.FindAllAsync(aqs, properties);

            foreach (var dev in collection)
            {
                string name = dev.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;

                bool isConnected = dev.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connObj) && connObj is bool b && b;

                int? battery = null;
                if (dev.Properties.TryGetValue(PKEY_BatteryLevel, out var battObj) && battObj != null)
                {
                    if (battObj is byte bVal) battery = bVal;
                    else if (battObj is int iVal) battery = iVal;
                }

                if (isConnected || battery.HasValue)
                {
                    var (category, glyph) = CategorizeDevice(name);
                    list.Add(new PeripheralDeviceInfo
                    {
                        Id = dev.Id,
                        Name = name,
                        Category = category,
                        Glyph = glyph,
                        BatteryPercent = battery ?? 80,
                        IsConnected = isConnected,
                        IsCharging = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PeripheralService] WinRT Device query: {ex.Message}");
        }
    }

    private static (string category, string glyph) CategorizeDevice(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("mouse") || lower.Contains("trackball") || lower.Contains("master"))
            return ("Mouse", "\uE962");
        if (lower.Contains("keyboard") || lower.Contains("keychron") || lower.Contains("k380"))
            return ("Keyboard", "\uE92E");
        if (lower.Contains("controller") || lower.Contains("gamepad") || lower.Contains("xbox") || lower.Contains("dualsense"))
            return ("Controller", "\uE7FC");
        if (lower.Contains("airpod") || lower.Contains("headphone") || lower.Contains("buds") || lower.Contains("wh-1000") || lower.Contains("audio") || lower.Contains("speaker"))
            return ("Headset", "\uE7F6");
        if (lower.Contains("phone") || lower.Contains("iphone") || lower.Contains("pixel") || lower.Contains("galaxy") || lower.Contains("s2") || lower.Contains("s24"))
            return ("Phone", "\uE8EA");
        if (lower.Contains("pen") || lower.Contains("stylus") || lower.Contains("surface pen"))
            return ("Stylus", "\uEDC6");

        return ("Peripheral", "\uE702");
    }

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
