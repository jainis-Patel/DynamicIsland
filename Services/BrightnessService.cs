using System.Windows.Threading;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Controls display brightness across both laptop internal panels (WMI <c>WmiMonitorBrightness</c> /
/// <c>WmiMonitorBrightnessMethods</c>) and external desktop monitors (Win32 DDC/CI <c>dxva2.dll</c>).
/// </summary>
internal sealed class BrightnessService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly Func<IntPtr> _getTargetMonitor;
    private readonly object _hwLock = new();
    private bool _disposed;
    private int _brightnessPercent = 80;

    public int BrightnessPercent => _brightnessPercent;
    public bool IsHardwareSupported { get; private set; }

    public event Action<int>? BrightnessChanged;

    public BrightnessService(Dispatcher ui, Func<IntPtr> getTargetMonitor)
    {
        _ui = ui;
        _getTargetMonitor = getTargetMonitor;
    }

    public void InitializeAsync()
    {
        Task.Run(() =>
        {
            if (_disposed) return;
            lock (_hwLock)
            {
                if (TryGetWmiBrightness(out int wmiPct))
                {
                    _brightnessPercent = Math.Clamp(wmiPct, 0, 100);
                    IsHardwareSupported = true;
                }
                else if (TryGetDdcBrightness(_getTargetMonitor(), out int ddcPct))
                {
                    _brightnessPercent = Math.Clamp(ddcPct, 0, 100);
                    IsHardwareSupported = true;
                }
            }

            _ = _ui.InvokeAsync(() => BrightnessChanged?.Invoke(_brightnessPercent));
        });
    }

    public int StepBrightness(int deltaPercent)
    {
        int next = Math.Clamp(_brightnessPercent + deltaPercent, 5, 100);
        SetBrightness(next);
        return next;
    }

    public void SetBrightness(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        _brightnessPercent = clamped;
        BrightnessChanged?.Invoke(clamped);

        IntPtr hMon = _getTargetMonitor();
        Task.Run(() =>
        {
            if (_disposed) return;
            lock (_hwLock)
            {
                if (!TrySetWmiBrightness(clamped))
                {
                    TrySetDdcBrightness(hMon, clamped);
                }
            }
        });
    }

    private static bool TryGetWmiBrightness(out int percent)
    {
        percent = 80;
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType == null) return false;
            dynamic? locator = Activator.CreateInstance(locatorType);
            if (locator == null) return false;

            dynamic service = locator.ConnectServer(".", @"root\wmi");
            dynamic instances = service.ExecQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True");
            foreach (dynamic item in instances)
            {
                percent = Convert.ToInt32(item.CurrentBrightness);
                return true;
            }
        }
        catch
        {
            // WMI brightness is only supported on internal laptop panels
        }
        return false;
    }

    private static bool TrySetWmiBrightness(int percent)
    {
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType == null) return false;
            dynamic? locator = Activator.CreateInstance(locatorType);
            if (locator == null) return false;

            dynamic service = locator.ConnectServer(".", @"root\wmi");
            dynamic instances = service.ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
            bool any = false;
            foreach (dynamic item in instances)
            {
                item.WmiSetBrightness(1, (byte)Math.Clamp(percent, 0, 100));
                any = true;
            }
            return any;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDdcBrightness(IntPtr hMonitor, out int percent)
    {
        percent = 80;
        if (hMonitor == IntPtr.Zero) return false;

        try
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                return false;

            var monitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                return false;

            try
            {
                for (int i = 0; i < monitors.Length; i++)
                {
                    if (GetMonitorBrightness(monitors[i].hPhysicalMonitor, out uint min, out uint cur, out uint max) && max > min)
                    {
                        percent = (int)Math.Round(((double)(cur - min) / (max - min)) * 100.0);
                        return true;
                    }
                }
            }
            finally
            {
                DestroyPhysicalMonitors(count, monitors);
            }
        }
        catch
        {
        }
        return false;
    }

    private static bool TrySetDdcBrightness(IntPtr hMonitor, int percent)
    {
        if (hMonitor == IntPtr.Zero) return false;

        try
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                return false;

            var monitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                return false;

            try
            {
                bool any = false;
                for (int i = 0; i < monitors.Length; i++)
                {
                    if (GetMonitorBrightness(monitors[i].hPhysicalMonitor, out uint min, out _, out uint max) && max > min)
                    {
                        uint target = min + (uint)Math.Round((percent / 100.0) * (max - min));
                        if (SetMonitorBrightness(monitors[i].hPhysicalMonitor, target))
                        {
                            any = true;
                        }
                    }
                    else if (SetMonitorBrightness(monitors[i].hPhysicalMonitor, (uint)Math.Clamp(percent, 0, 100)))
                    {
                        any = true;
                    }
                }
                return any;
            }
            finally
            {
                DestroyPhysicalMonitors(count, monitors);
            }
        }
        catch
        {
        }
        return false;
    }

    public static string GlyphForBrightness(int percent) => percent switch
    {
        >= 65 => "\uE706",
        >= 30 => "\uE706",
        _ => "\uEC8A"
    };

    public void Dispose()
    {
        _disposed = true;
    }
}
