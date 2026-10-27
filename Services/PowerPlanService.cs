using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.System.Power;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

public sealed record PowerPlanOption(string Key, string Name, Guid Guid, string Icon);

public sealed class PowerStatusInfo
{
    public bool HasBattery { get; set; }
    public bool IsCharging { get; set; }
    public bool IsAcOnline { get; set; }
    public int BatteryPercent { get; set; } = 100;
    public string TimeEstimateText { get; set; } = "Continuous AC Power";
    public string StatusSubtitle { get; set; } = "Desktop Power";
    public string ActiveSchemeKey { get; set; } = "Balanced";
    public string ActiveSchemeName { get; set; } = "Balanced";
    public string BatteryGlyph { get; set; } = "\uEBB5";
}

/// <summary>
/// Monitors laptop and desktop power vitals, calculates charging/discharging time estimates,
/// triggers iOS-style fluid charging cable pulses, and switches Windows power schemes with 1 click.
/// </summary>
internal sealed class PowerPlanService : IDisposable
{
    public static readonly Guid SchemePerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid SchemeBalanced    = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid SchemePowerSaver  = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    public static readonly IReadOnlyList<PowerPlanOption> AvailablePlans = new[]
    {
        new PowerPlanOption("Performance", "Best Performance", SchemePerformance, "\uE945"),
        new PowerPlanOption("Balanced", "Balanced", SchemeBalanced, "\uE74C"),
        new PowerPlanOption("PowerSaver", "Power Saver", SchemePowerSaver, "\uEC0A")
    };

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _pollTimer;
    private bool _lastAcOnline;
    private int _lastPercent = -1;
    private bool _initialized;
    private bool _disposed;

    public PowerStatusInfo Current { get; private set; } = new();

    public event Action<PowerStatusInfo>? StatusChanged;
    public event Action<int, string>? ChargingPulseTriggered;
    public event Action<int, string>? DisconnectedTriggered;
    public event Action<int>? LowBatteryTriggered;

    public PowerPlanService(Dispatcher ui)
    {
        _ui = ui;
        _pollTimer = new DispatcherTimer(TimeSpan.FromSeconds(2.5), DispatcherPriority.Background, (_, _) => Refresh(), _ui);

        try
        {
            if (PowerManager.BatteryStatus != BatteryStatus.NotPresent)
            {
                PowerManager.PowerSupplyStatusChanged += OnWinRTPowerSupplyChanged;
                PowerManager.RemainingChargePercentChanged += OnWinRTRemainingPercentChanged;
            }
        }
        catch { }

        Refresh();
        _initialized = true;
    }

    private void OnWinRTPowerSupplyChanged(object? sender, object e) => _ui.InvokeAsync(Refresh);
    private void OnWinRTRemainingPercentChanged(object? sender, object e) => _ui.InvokeAsync(Refresh);

    public void Refresh()
    {
        if (_disposed) return;

        bool hasStatus = GetSystemPowerStatus(out var status);
        Guid activeGuid = GetCurrentActiveGuid();

        string schemeKey = "Balanced";
        string schemeName = "Balanced";
        if (activeGuid == SchemePerformance)
        {
            schemeKey = "Performance";
            schemeName = "Best Performance";
        }
        else if (activeGuid == SchemePowerSaver)
        {
            schemeKey = "PowerSaver";
            schemeName = "Power Saver";
        }

        bool hasBattery = hasStatus && (status.BatteryFlag != 128 && status.BatteryLifePercent != 255);
        bool isAcOnline = hasStatus && status.ACLineStatus == 1;
        int percent = hasBattery ? status.BatteryLifePercent : 100;
        bool isCharging = hasBattery && isAcOnline && ((status.BatteryFlag & 8) != 0 || percent < 100);

        string timeEstimate;
        string subtitle;
        if (hasBattery)
        {
            if (isCharging)
            {
                subtitle = percent >= 100 ? "Fully Charged" : "Connected to AC · Fast Charging";
                if (percent >= 100)
                {
                    timeEstimate = "100% · Battery Healthy";
                }
                else
                {
                    // Estimate charging time (typical laptop ~1.2 mins per 1% from 80W)
                    int remainingPct = 100 - percent;
                    int estMins = (int)Math.Max(5, remainingPct * 1.3);
                    timeEstimate = estMins >= 60
                        ? $"Estimated ~{estMins / 60}h {estMins % 60}m to full"
                        : $"Estimated ~{estMins} min to full";
                }
            }
            else
            {
                subtitle = "On Battery";
                if (status.BatteryLifeTime > 0)
                {
                    var span = TimeSpan.FromSeconds(status.BatteryLifeTime);
                    timeEstimate = span.TotalHours >= 1
                        ? $"Estimated ~{(int)span.TotalHours}h {span.Minutes}m remaining"
                        : $"Estimated ~{span.Minutes} min remaining";
                }
                else
                {
                    timeEstimate = $"{percent}% remaining";
                }
            }
        }
        else
        {
            subtitle = "Desktop Continuous AC Power";
            timeEstimate = "Connected to Wall Outlet · Unlimited";
        }

        string glyph = BatteryGlyphFor(percent, isCharging, isAcOnline, hasBattery);

        Current = new PowerStatusInfo
        {
            HasBattery = hasBattery,
            IsCharging = isCharging,
            IsAcOnline = isAcOnline,
            BatteryPercent = percent,
            TimeEstimateText = timeEstimate,
            StatusSubtitle = subtitle,
            ActiveSchemeKey = schemeKey,
            ActiveSchemeName = schemeName,
            BatteryGlyph = glyph
        };

        // Detect charger connection/disconnection transitions
        if (_initialized)
        {
            if (!_lastAcOnline && isAcOnline && hasBattery)
            {
                ChargingPulseTriggered?.Invoke(percent, timeEstimate);
            }
            else if (_lastAcOnline && !isAcOnline && hasBattery)
            {
                DisconnectedTriggered?.Invoke(percent, timeEstimate);
            }

            if (!isAcOnline && hasBattery && _lastPercent > 20 && percent <= 20)
            {
                LowBatteryTriggered?.Invoke(percent);
            }
        }

        _lastAcOnline = isAcOnline;
        _lastPercent = percent;

        StatusChanged?.Invoke(Current);
    }

    public bool SetActiveScheme(string schemeKey)
    {
        try
        {
            Guid target = schemeKey switch
            {
                "Performance" => SchemePerformance,
                "PowerSaver" => SchemePowerSaver,
                _ => SchemeBalanced
            };

            uint res = PowerSetActiveScheme(IntPtr.Zero, ref target);
            if (res == 0)
            {
                App.Log($"[PowerPlanService] Switched power scheme to: {schemeKey}");
                Refresh();
                return true;
            }
            else
            {
                App.Log($"[PowerPlanService] PowerSetActiveScheme returned error {res}");
                return false;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PowerPlanService] SetActiveScheme exception: {ex.Message}");
            return false;
        }
    }

    public void TriggerSimulatedChargingPulse()
    {
        int pct = Current.HasBattery ? Current.BatteryPercent : 88;
        string est = Current.HasBattery ? Current.TimeEstimateText : "Estimated ~32 min to full";
        ChargingPulseTriggered?.Invoke(pct, est);
    }

    private static Guid GetCurrentActiveGuid()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr guidPtr) == 0 && guidPtr != IntPtr.Zero)
            {
                try
                {
                    return Marshal.PtrToStructure<Guid>(guidPtr);
                }
                finally
                {
                    LocalFree(guidPtr);
                }
            }
        }
        catch { }
        return SchemeBalanced;
    }

    private static string BatteryGlyphFor(int percent, bool isCharging, bool isAcOnline, bool hasBattery)
    {
        if (!hasBattery) return "\uE945"; // Lightning / AC Power
        if (isCharging) return "\uE945";

        int bracket = Math.Clamp(percent / 10, 0, 10);
        return ((char)(0xE850 + bracket)).ToString();
    }

    public void Dispose()
    {
        _disposed = true;
        _pollTimer.Stop();
        try
        {
            PowerManager.PowerSupplyStatusChanged -= OnWinRTPowerSupplyChanged;
            PowerManager.RemainingChargePercentChanged -= OnWinRTRemainingPercentChanged;
        }
        catch { }
    }
}
