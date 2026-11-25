using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Core;
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
    private volatile bool _disposed;
    private int _brightnessPercent = 80;
    private int _pendingBrightness = -1;
    private int _writerActive;
    private IntPtr _pendingMonitor;

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

        _pendingMonitor = _getTargetMonitor();
        Volatile.Write(ref _pendingBrightness, clamped);

        if (!_disposed && Interlocked.CompareExchange(ref _writerActive, 1, 0) == 0)
        {
            Task.Run(ProcessBrightnessWrites).FireAndForget("Brightness.Write");
        }
    }

    private void ProcessBrightnessWrites()
    {
        try
        {
            while (!_disposed)
            {
                int target = Interlocked.Exchange(ref _pendingBrightness, -1);
                if (target < 0) break;

                IntPtr hMon = _pendingMonitor;
                lock (_hwLock)
                {
                    if (!TrySetWmiBrightness(target))
                    {
                        TrySetDdcBrightness(hMon, target);
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _writerActive, 0);
            if (!_disposed && Volatile.Read(ref _pendingBrightness) >= 0 &&
                Interlocked.CompareExchange(ref _writerActive, 1, 0) == 0)
            {
                Task.Run(ProcessBrightnessWrites).FireAndForget("Brightness.Write");
            }
        }
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            try { Marshal.FinalReleaseComObject(obj); } catch { }
        }
    }

    private static bool TryGetWmiBrightness(out int percent)
    {
        percent = 80;
        object? locator = null;
        object? service = null;
        object? instances = null;
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType == null) return false;
            locator = Activator.CreateInstance(locatorType);
            if (locator == null) return false;

            service = ((dynamic)locator).ConnectServer(".", @"root\wmi");
            instances = ((dynamic)service).ExecQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True");
            foreach (dynamic item in (dynamic)instances)
            {
                try
                {
                    percent = Convert.ToInt32(item.CurrentBrightness);
                    return true;
                }
                finally
                {
                    ReleaseCom(item);
                }
            }
        }
        catch
        {
            // WMI brightness is only supported on internal laptop panels
        }
        finally
        {
            ReleaseCom(instances);
            ReleaseCom(service);
            ReleaseCom(locator);
        }
        return false;
    }

    private static bool TrySetWmiBrightness(int percent)
    {
        object? locator = null;
        object? service = null;
        object? instances = null;
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType == null) return false;
            locator = Activator.CreateInstance(locatorType);
            if (locator == null) return false;

            service = ((dynamic)locator).ConnectServer(".", @"root\wmi");
            instances = ((dynamic)service).ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
            bool any = false;
            foreach (dynamic item in (dynamic)instances)
            {
                try
                {
                    item.WmiSetBrightness(1, (byte)Math.Clamp(percent, 0, 100));
                    any = true;
                }
                finally
                {
                    ReleaseCom(item);
                }
            }
            return any;
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseCom(instances);
            ReleaseCom(service);
            ReleaseCom(locator);
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
