using System.Runtime.InteropServices;
using System.Windows.Threading;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Ultra-lightweight CPU and RAM telemetry reader using Win32 GetSystemTimes and GlobalMemoryStatusEx.
/// Consumes 0% CPU compared to PerformanceCounter.
/// </summary>
internal sealed class SystemTelemetryService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private ulong _prevIdleTime;
    private ulong _prevTotalTime;

    public int CpuPercent { get; private set; }
    public int MemoryPercent { get; private set; }
    public double MemoryUsedGb { get; private set; }
    public double MemoryTotalGb { get; private set; }

    public event Action? Updated;

    public SystemTelemetryService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Sample();
        Sample();
    }

    /// <summary>
    /// Enables or pauses the 1 Hz CPU/RAM telemetry timer so no syscalls or string allocations
    /// occur while the Island is in Idle, Compact, Toast, or Hidden states.
    /// </summary>
    public void SetActive(bool active)
    {
        if (active && !_timer.IsEnabled)
        {
            Sample();
            _timer.Start();
        }
        else if (!active && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    private void Sample()
    {
        int nextCpu = CpuPercent;
        int nextMem = MemoryPercent;

        // ── CPU ──
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong idleTime = idle.ToUInt64();
            ulong totalTime = kernel.ToUInt64() + user.ToUInt64();

            if (_prevTotalTime > 0 && totalTime > _prevTotalTime)
            {
                ulong idleDelta = idleTime - _prevIdleTime;
                ulong totalDelta = totalTime - _prevTotalTime;

                if (totalDelta > 0)
                {
                    double usage = 1.0 - ((double)idleDelta / totalDelta);
                    nextCpu = Math.Clamp((int)Math.Round(usage * 100), 0, 100);
                }
            }

            _prevIdleTime = idleTime;
            _prevTotalTime = totalTime;
        }

        // ── RAM ──
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            nextMem = (int)mem.dwMemoryLoad;
            ulong usedBytes = mem.ullTotalPhys - mem.ullAvailPhys;
            MemoryUsedGb = Math.Round(usedBytes / (1024.0 * 1024 * 1024), 1);
            MemoryTotalGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024 * 1024), 1);
        }

        if (nextCpu != CpuPercent || nextMem != MemoryPercent)
        {
            CpuPercent = nextCpu;
            MemoryPercent = nextMem;
            Updated?.Invoke();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
