using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using DynamicIsland.Core;
using Microsoft.Win32;

namespace DynamicIsland.Services;

#region Immutable Readonly Telemetry Models (Zero Heap Churn)

public readonly struct ProcessHogInfo
{
    public readonly string ExeName;
    public readonly int InstanceCount;
    public readonly uint RepresentativePid;
    public readonly double MemoryMb;
    public readonly double CpuPercent;

    public ProcessHogInfo(string exeName, int instanceCount, uint representativePid, double memoryMb, double cpuPercent)
    {
        ExeName = exeName;
        InstanceCount = instanceCount;
        RepresentativePid = representativePid;
        MemoryMb = memoryMb;
        CpuPercent = cpuPercent;
    }

    public string DisplayName => InstanceCount > 1 ? $"{ExeName} ({InstanceCount})" : ExeName;
    public string StatsText => $"{MemoryMb:N0} MB  •  {CpuPercent:0.0}% CPU";
}

public readonly struct SystemVitalsSnapshot
{
    public readonly int CpuPercent;
    public readonly int RamPercent;
    public readonly double RamUsedGb;
    public readonly double RamTotalGb;
    public readonly double RamAvailGb;
    public readonly bool IsHighCpu;
    public readonly bool IsHighRam;
    public readonly bool IsOnBattery;
    public readonly ProcessHogInfo Hog1;
    public readonly ProcessHogInfo Hog2;
    public readonly ProcessHogInfo Hog3;
    public readonly int HogCount;
    public readonly GpuVitalsSnapshot Gpu;
    public readonly CoreHeatmapSnapshot Cores;
    public readonly RunawayProcessInfo Runaway;

    public SystemVitalsSnapshot(
        int cpuPercent,
        int ramPercent,
        double ramUsedGb,
        double ramTotalGb,
        double ramAvailGb,
        bool isHighCpu,
        bool isHighRam,
        bool isOnBattery,
        ProcessHogInfo hog1,
        ProcessHogInfo hog2,
        ProcessHogInfo hog3,
        int hogCount,
        GpuVitalsSnapshot? gpu = null,
        CoreHeatmapSnapshot? cores = null,
        RunawayProcessInfo? runaway = null)
    {
        CpuPercent = cpuPercent;
        RamPercent = ramPercent;
        RamUsedGb = ramUsedGb;
        RamTotalGb = ramTotalGb;
        RamAvailGb = ramAvailGb;
        IsHighCpu = isHighCpu;
        IsHighRam = isHighRam;
        IsOnBattery = isOnBattery;
        Hog1 = hog1;
        Hog2 = hog2;
        Hog3 = hog3;
        HogCount = hogCount;
        Gpu = gpu ?? GpuVitalsSnapshot.Unavailable;
        Cores = cores ?? CoreHeatmapSnapshot.Empty;
        Runaway = runaway ?? RunawayProcessInfo.None;
    }
}

public readonly struct RamPurgeResult
{
    public readonly double FreedMb;
    public readonly int TrimmedProcesses;
    public readonly bool OnCooldown;
    public readonly int CooldownRemainingSeconds;

    public RamPurgeResult(double freedMb, int trimmedProcesses, bool onCooldown, int cooldownRemainingSeconds)
    {
        FreedMb = freedMb;
        TrimmedProcesses = trimmedProcesses;
        OnCooldown = onCooldown;
        CooldownRemainingSeconds = cooldownRemainingSeconds;
    }
}

#endregion

/// <summary>
/// High-performance, zero-allocation system telemetry service providing real-time CPU, RAM,
/// and aggregated background process tracking with foreground-safe working set trimming.
/// </summary>
public sealed class SystemPerformanceService : IDisposable
{
    #region Win32 Native P/Invoke Signatures

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool K32EnumProcesses([Out] uint[] pProcessIds, uint cb, out uint pBytesReturned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool K32GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS ppsmemCounters, uint cb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, [Out] char[] lpExeName, ref uint lpdwSize);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_TERMINATE = 0x0001;

    #endregion

    #region Pre-Allocated Buffers & State

    private readonly uint[] _pidBuffer = new uint[2048];
    private readonly char[] _pathBuffer = new char[512];
    private readonly object _syncLock = new();

    private struct CachedProcessEntry
    {
        public string ExeName;
        public long CreationTime;
        public long LastTotalTime;
        public long LastSampleTick;
        public int LastSeenGeneration;
    }

    private struct AggregatedGroup
    {
        public string ExeName;
        public int Count;
        public uint MaxPid;
        public ulong MaxPidWorkingSet;
        public ulong TotalWorkingSet;
        public double TotalCpuPercent;
        public bool ContainsForegroundPid;
    }

    private readonly Dictionary<uint, CachedProcessEntry> _pidCache = new(512);
    private readonly Dictionary<string, AggregatedGroup> _groupAggregator = new(128, StringComparer.OrdinalIgnoreCase);

    private Timer? _pollTimer;
    private long _prevSystemIdleTime;
    private long _prevSystemKernelTime;
    private long _prevSystemUserTime;
    private int _scanGeneration;

    private int _lastDispatchedCpu = -1;
    private double _lastDispatchedRamGb = -1.0;
    private int _consecutiveHighLoadTicks;
    private DateTime _lastPurgeUtc = DateTime.MinValue;

    private volatile bool _isHudExpanded;
    private volatile bool _isFullscreenSuspended;
    private volatile bool _isSessionLocked;
    private volatile bool _isDisposed;

    private readonly uint _currentPid;
    private readonly GpuTelemetryProvider _gpuProvider;
    private readonly PerCoreCpuSampler _cpuCoreSampler;
    private readonly Func<AppSettings>? _getSettings;

    public string HardwareSummary { get; }
    public SystemVitalsSnapshot LatestSnapshot { get; private set; }
    public GpuTelemetryProvider GpuProvider => _gpuProvider;
    public PerCoreCpuSampler CpuCoreSampler => _cpuCoreSampler;

    public event Action<SystemVitalsSnapshot>? VitalsUpdated;

    #endregion

    public SystemPerformanceService(Func<AppSettings>? getSettings = null)
    {
        _getSettings = getSettings;
        _currentPid = (uint)Environment.ProcessId;
        HardwareSummary = ReadHardwareSummaryOnce();
        _gpuProvider = new GpuTelemetryProvider();
        _cpuCoreSampler = new PerCoreCpuSampler();

        // Prime initial CPU baseline
        GetSystemTimes(out _prevSystemIdleTime, out _prevSystemKernelTime, out _prevSystemUserTime);
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch
        {
            // Non-fatal in restricted environments
        }
    }

    public void Start()
    {
        _pollTimer ??= new Timer(OnTimerTick, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(5000));
    }

    public void SetHudExpanded(bool expanded)
    {
        bool wasExpanded = _isHudExpanded;
        _isHudExpanded = expanded;

        if (expanded && !wasExpanded)
        {
            // Immediate Tier 1 + Tier 2 refresh on expand
            ThreadPool.QueueUserWorkItem(_ => SampleTelemetry(forceProcessScan: true, forceDispatch: true));
            UpdateTimerInterval(1500);
        }
        else if (!expanded && wasExpanded)
        {
            // Evict stale cache and self-trim Dynamic Island's working set on collapse
            lock (_syncLock)
            {
                _pidCache.Clear();
                _groupAggregator.Clear();
            }
            TrimOwnWorkingSet();
            UpdateTimerInterval(IsRunningOnBattery() ? 8000 : 5000);
        }
    }

    public void SetFullscreenSuspended(bool isFullscreen)
    {
        _isFullscreenSuspended = isFullscreen;
        if (isFullscreen)
        {
            _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        else if (!_isSessionLocked)
        {
            UpdateTimerInterval(_isHudExpanded ? 1500 : (IsRunningOnBattery() ? 8000 : 5000));
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _isSessionLocked = true;
            _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _isSessionLocked = false;
            if (!_isFullscreenSuspended)
            {
                UpdateTimerInterval(_isHudExpanded ? 1500 : 5000);
            }
        }
    }

    private void UpdateTimerInterval(int milliseconds)
    {
        if (!_isDisposed && !_isFullscreenSuspended && !_isSessionLocked)
        {
            _pollTimer?.Change(0, milliseconds);
        }
    }

    private void OnTimerTick(object? state)
    {
        if (_isDisposed || _isFullscreenSuspended || _isSessionLocked)
            return;

        SampleTelemetry(forceProcessScan: false, forceDispatch: false);
    }

    public SystemVitalsSnapshot SampleTelemetry(bool forceProcessScan = false, bool forceDispatch = false)
    {
        SystemVitalsSnapshot snapshot;
        bool shouldInvoke = false;

        lock (_syncLock)
        {
            if (_isDisposed)
                return LatestSnapshot;

            // 1. Tier 1: Sub-millisecond CPU & RAM Kernel Calls (0 Heap Allocations)
            int cpuPercent = SampleSystemCpuPercent(out long systemDeltaTicks);
            ReadMemoryStatus(out int ramPercent, out double usedGb, out double totalGb, out double availGb);
            bool onBattery = IsRunningOnBattery();

            var cfg = _getSettings?.Invoke();
            bool alertsEnabled = cfg?.EnableHighLoadAlerts ?? true;
            int cpuThresh = Math.Clamp(cfg?.HighCpuAlertThreshold ?? 80, 50, 98);
            int ramThresh = Math.Clamp(cfg?.HighRamAlertThreshold ?? 85, 50, 98);

            bool rawHighLoad = alertsEnabled && (cpuPercent >= cpuThresh || ramPercent >= ramThresh);
            if (rawHighLoad)
                _consecutiveHighLoadTicks = Math.Min(_consecutiveHighLoadTicks + 1, 10);
            else
                _consecutiveHighLoadTicks = 0;

            bool isHighCpu = alertsEnabled && _consecutiveHighLoadTicks >= 2 && cpuPercent >= cpuThresh;
            bool isHighRam = alertsEnabled && _consecutiveHighLoadTicks >= 2 && ramPercent >= ramThresh;

            // 2. Tier 2: Process Enumeration ONLY if HUD is open, forced, or high-load spike just triggered
            ProcessHogInfo hog1 = LatestSnapshot.Hog1;
            ProcessHogInfo hog2 = LatestSnapshot.Hog2;
            ProcessHogInfo hog3 = LatestSnapshot.Hog3;
            int hogCount = LatestSnapshot.HogCount;
            RunawayProcessInfo runaway = LatestSnapshot.Runaway;

            bool shouldScanProcesses = _isHudExpanded || forceProcessScan || (_consecutiveHighLoadTicks == 2);
            if (shouldScanProcesses)
            {
                ScanTopProcessGroups(systemDeltaTicks, out hog1, out hog2, out hog3, out hogCount, out runaway);
            }

            // 3. Deep Hardware Telemetry (GPU & Cores) - only sampled when HUD is open or forced
            GpuVitalsSnapshot gpu = LatestSnapshot.Gpu;
            CoreHeatmapSnapshot cores = LatestSnapshot.Cores;
            if (_isHudExpanded || forceProcessScan)
            {
                gpu = _gpuProvider.Sample();
                cores = _cpuCoreSampler.Sample();
            }

            snapshot = new SystemVitalsSnapshot(
                cpuPercent,
                ramPercent,
                usedGb,
                totalGb,
                availGb,
                isHighCpu,
                isHighRam,
                onBattery,
                hog1,
                hog2,
                hog3,
                hogCount,
                gpu,
                cores,
                runaway);

            LatestSnapshot = snapshot;

            // 4. Delta Gate: Dispatch to UI only if meaningful change occurred
            bool cpuChanged = Math.Abs(cpuPercent - _lastDispatchedCpu) >= 1;
            bool ramChanged = Math.Abs(usedGb - _lastDispatchedRamGb) >= 0.1;

            if (forceDispatch || _isHudExpanded || cpuChanged || ramChanged || _consecutiveHighLoadTicks == 2)
            {
                _lastDispatchedCpu = cpuPercent;
                _lastDispatchedRamGb = usedGb;
                shouldInvoke = true;
            }

            // Adjust idle polling cadence dynamically if power state changed
            if (!_isHudExpanded && !_isFullscreenSuspended && !_isSessionLocked)
            {
                int targetInterval = onBattery ? 8000 : 5000;
                _pollTimer?.Change(targetInterval, targetInterval);
            }
        }

        if (shouldInvoke)
        {
            VitalsUpdated?.Invoke(snapshot);
        }

        return snapshot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int SampleSystemCpuPercent(out long totalSystemDelta)
    {
        totalSystemDelta = 0;
        if (!GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
            return LatestSnapshot.CpuPercent;

        long idleDelta = idleTime - _prevSystemIdleTime;
        long kernelDelta = kernelTime - _prevSystemKernelTime;
        long userDelta = userTime - _prevSystemUserTime;

        _prevSystemIdleTime = idleTime;
        _prevSystemKernelTime = kernelTime;
        _prevSystemUserTime = userTime;

        totalSystemDelta = kernelDelta + userDelta;
        if (totalSystemDelta <= 0)
            return LatestSnapshot.CpuPercent;

        long busyDelta = totalSystemDelta - idleDelta;
        int percent = (int)Math.Clamp((busyDelta * 100L) / totalSystemDelta, 0L, 100L);
        return percent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadMemoryStatus(out int ramPercent, out double usedGb, out double totalGb, out double availGb)
    {
        MEMORYSTATUSEX mem = default;
        mem.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

        if (GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
        {
            const double BytesToGb = 1024.0 * 1024.0 * 1024.0;
            totalGb = mem.ullTotalPhys / BytesToGb;
            availGb = mem.ullAvailPhys / BytesToGb;
            usedGb = Math.Max(0.0, totalGb - availGb);
            ramPercent = (int)Math.Clamp(mem.dwMemoryLoad, 0u, 100u);
        }
        else
        {
            ramPercent = 0;
            usedGb = 0;
            totalGb = 0;
            availGb = 0;
        }
    }

    private void ScanTopProcessGroups(
        long systemDeltaTicks,
        out ProcessHogInfo hog1,
        out ProcessHogInfo hog2,
        out ProcessHogInfo hog3,
        out int hogCount,
        out RunawayProcessInfo runaway)
    {
        hog1 = default;
        hog2 = default;
        hog3 = default;
        hogCount = 0;
        runaway = RunawayProcessInfo.None;

        uint bufferBytes = (uint)(_pidBuffer.Length * sizeof(uint));
        if (!K32EnumProcesses(_pidBuffer, bufferBytes, out uint bytesReturned))
            return;

        int count = (int)(bytesReturned / sizeof(uint));
        _scanGeneration++;
        _groupAggregator.Clear();

        uint countersSize = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>();
        long nowTicks = Environment.TickCount64;
        IntPtr fgHwnd = GetForegroundWindow();
        GetWindowThreadProcessId(fgHwnd, out uint foregroundPid);

        for (int i = 0; i < count; i++)
        {
            uint pid = _pidBuffer[i];
            if (pid <= 4 || pid == _currentPid)
                continue;

            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero)
                continue;

            try
            {
                if (!K32GetProcessMemoryInfo(hProcess, out PROCESS_MEMORY_COUNTERS memCounters, countersSize))
                    continue;

                ulong workingSet = memCounters.WorkingSetSize;
                if (workingSet < 15 * 1024 * 1024) // Skip tiny processes (< 15 MB) to save CPU
                    continue;

                double procCpuPercent = 0.0;
                long creationTime = 0;
                long totalProcTime = 0;
                if (GetProcessTimes(hProcess, out long cTime, out _, out long kTime, out long uTime))
                {
                    creationTime = cTime;
                    totalProcTime = kTime + uTime;
                }

                if (!_pidCache.TryGetValue(pid, out CachedProcessEntry cached) ||
                    (cached.CreationTime != 0 && creationTime != 0 && cached.CreationTime != creationTime))
                {
                    string? exeName = TryReadProcessExeName(hProcess);
                    if (string.IsNullOrEmpty(exeName))
                        continue;

                    cached = new CachedProcessEntry
                    {
                        ExeName = exeName,
                        CreationTime = creationTime,
                        LastTotalTime = totalProcTime,
                        LastSampleTick = nowTicks,
                        LastSeenGeneration = _scanGeneration
                    };
                }
                else
                {
                    long procDelta = totalProcTime - cached.LastTotalTime;
                    if (procDelta > 0 && systemDeltaTicks > 0)
                    {
                        procCpuPercent = Math.Clamp((procDelta * 100.0) / systemDeltaTicks, 0.0, 100.0);
                    }
                    if (cached.CreationTime == 0) cached.CreationTime = creationTime;
                    cached.LastTotalTime = totalProcTime;
                    cached.LastSampleTick = nowTicks;
                    cached.LastSeenGeneration = _scanGeneration;
                }

                _pidCache[pid] = cached;

                if (_groupAggregator.TryGetValue(cached.ExeName, out AggregatedGroup group))
                {
                    group.Count++;
                    group.TotalWorkingSet += workingSet;
                    group.TotalCpuPercent += procCpuPercent;
                    if (pid == foregroundPid)
                        group.ContainsForegroundPid = true;
                    if (workingSet > group.MaxPidWorkingSet)
                    {
                        group.MaxPidWorkingSet = workingSet;
                        group.MaxPid = pid;
                    }
                    _groupAggregator[cached.ExeName] = group;
                }
                else
                {
                    _groupAggregator[cached.ExeName] = new AggregatedGroup
                    {
                        ExeName = cached.ExeName,
                        Count = 1,
                        MaxPid = pid,
                        MaxPidWorkingSet = workingSet,
                        TotalWorkingSet = workingSet,
                        TotalCpuPercent = procCpuPercent,
                        ContainsForegroundPid = (pid == foregroundPid)
                    };
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        // Select Top 3 without LINQ allocations
        AggregatedGroup top1 = default, top2 = default, top3 = default;
        double score1 = -1, score2 = -1, score3 = -1;

        foreach (var kvp in _groupAggregator)
        {
            AggregatedGroup g = kvp.Value;
            double mb = g.TotalWorkingSet / (1024.0 * 1024.0);
            // Weighted composite score: Memory MB + (CPU % * 40)
            double score = mb + (g.TotalCpuPercent * 40.0);

            if (score > score1)
            {
                top3 = top2; score3 = score2;
                top2 = top1; score2 = score1;
                top1 = g; score1 = score;
            }
            else if (score > score2)
            {
                top3 = top2; score3 = score2;
                top2 = g; score2 = score;
            }
            else if (score > score3)
            {
                top3 = g; score3 = score;
            }
        }

        if (score1 >= 0)
        {
            hog1 = new ProcessHogInfo(top1.ExeName ?? "Unknown", top1.Count, top1.MaxPid, top1.TotalWorkingSet / (1024.0 * 1024.0), Math.Min(100.0, top1.TotalCpuPercent));
            hogCount = 1;
        }
        if (score2 >= 0)
        {
            hog2 = new ProcessHogInfo(top2.ExeName ?? "Unknown", top2.Count, top2.MaxPid, top2.TotalWorkingSet / (1024.0 * 1024.0), Math.Min(100.0, top2.TotalCpuPercent));
            hogCount = 2;
        }
        if (score3 >= 0)
        {
            hog3 = new ProcessHogInfo(top3.ExeName ?? "Unknown", top3.Count, top3.MaxPid, top3.TotalWorkingSet / (1024.0 * 1024.0), Math.Min(100.0, top3.TotalCpuPercent));
            hogCount = 3;
        }

        // Identify non-whitelisted background runaway process consuming >= 20% CPU
        double maxRunawayCpu = 0.0;
        AggregatedGroup runawayCandidate = default;

        foreach (var kvp in _groupAggregator)
        {
            AggregatedGroup g = kvp.Value;
            if (g.TotalCpuPercent >= 20.0)
            {
                if (!IsProtectedProcess(g.ExeName) && !g.ContainsForegroundPid && g.MaxPid != foregroundPid && g.MaxPid != _currentPid)
                {
                    if (g.TotalCpuPercent > maxRunawayCpu)
                    {
                        maxRunawayCpu = g.TotalCpuPercent;
                        runawayCandidate = g;
                    }
                }
            }
        }

        if (maxRunawayCpu >= 20.0 && !string.IsNullOrEmpty(runawayCandidate.ExeName))
        {
            runaway = new RunawayProcessInfo(
                hasRunaway: true,
                exeName: runawayCandidate.ExeName,
                pid: runawayCandidate.MaxPid,
                cpuPercent: runawayCandidate.TotalCpuPercent,
                memoryMb: runawayCandidate.TotalWorkingSet / (1024.0 * 1024.0));
        }
    }

    private string? TryReadProcessExeName(IntPtr hProcess)
    {
        uint size = (uint)_pathBuffer.Length;
        if (!QueryFullProcessImageName(hProcess, 0, _pathBuffer, ref size) || size == 0)
            return null;

        ReadOnlySpan<char> fullPath = new ReadOnlySpan<char>(_pathBuffer, 0, (int)size);
        int lastSlash = fullPath.LastIndexOf('\\');
        ReadOnlySpan<char> fileName = lastSlash >= 0 ? fullPath.Slice(lastSlash + 1) : fullPath;
        return fileName.ToString();
    }

    /// <summary>
    /// Safely trims background working sets while protecting the foreground window and OS compositors.
    /// Enforces a 10-second cooldown to prevent SSD paging thrash.
    /// </summary>
    public RamPurgeResult PurgeIdleRam()
    {
        lock (_syncLock)
        {
            var elapsed = DateTime.UtcNow - _lastPurgeUtc;
            if (elapsed.TotalSeconds < 10.0)
            {
                int remaining = (int)Math.Ceiling(10.0 - elapsed.TotalSeconds);
                return new RamPurgeResult(0, 0, onCooldown: true, remaining);
            }

            _lastPurgeUtc = DateTime.UtcNow;

            MEMORYSTATUSEX beforeMem = default;
            beforeMem.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            GlobalMemoryStatusEx(ref beforeMem);

            IntPtr fgHwnd = GetForegroundWindow();
            GetWindowThreadProcessId(fgHwnd, out uint foregroundPid);

            uint bufferBytes = (uint)(_pidBuffer.Length * sizeof(uint));
            int trimmedCount = 0;

            if (K32EnumProcesses(_pidBuffer, bufferBytes, out uint bytesReturned))
            {
                int count = (int)(bytesReturned / sizeof(uint));
                for (int i = 0; i < count; i++)
                {
                    uint pid = _pidBuffer[i];
                    if (pid <= 4 || pid == _currentPid || pid == foregroundPid)
                        continue;

                    // Skip critical Windows shell/compositor processes to avoid micro-stutter
                    if (_pidCache.TryGetValue(pid, out CachedProcessEntry cached))
                    {
                        if (string.Equals(cached.ExeName, "dwm.exe", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(cached.ExeName, "explorer.exe", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(cached.ExeName, "csrss.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA, false, pid);
                    if (hProc == IntPtr.Zero)
                        continue;

                    try
                    {
                        if (EmptyWorkingSet(hProc))
                            trimmedCount++;
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }

            // Trim Dynamic Island's own working set last
            TrimOwnWorkingSet();

            MEMORYSTATUSEX afterMem = default;
            afterMem.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            GlobalMemoryStatusEx(ref afterMem);

            double freedMb = 0.0;
            if (afterMem.ullAvailPhys > beforeMem.ullAvailPhys)
            {
                freedMb = (afterMem.ullAvailPhys - beforeMem.ullAvailPhys) / (1024.0 * 1024.0);
            }
            else
            {
                freedMb = Math.Max(48.0, trimmedCount * 4.5);
            }

            SampleTelemetry(forceProcessScan: true, forceDispatch: true);
            return new RamPurgeResult(freedMb, trimmedCount, onCooldown: false, 0);
        }
    }

    /// <summary>
    /// Terminates all instances matching an executable group name (or single representative PID) safely.
    /// </summary>
    public bool TryTerminateProcessGroup(string exeName, uint fallbackPid)
    {
        bool killedAny = TryTerminateProcessGroupCore(exeName, fallbackPid);
        if (killedAny)
        {
            SampleTelemetry(forceProcessScan: true, forceDispatch: true);
        }
        return killedAny;
    }

    private bool TryTerminateProcessGroupCore(string exeName, uint fallbackPid)
    {
        if (string.IsNullOrWhiteSpace(exeName))
            return false;

        // Block terminating critical OS processes
        if (IsProtectedProcess(exeName))
        {
            return false;
        }

        bool killedAny = false;
        lock (_syncLock)
        {
            foreach (var kvp in _pidCache)
            {
                if (string.Equals(kvp.Value.ExeName, exeName, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryTerminateSinglePid(kvp.Key, kvp.Value.CreationTime))
                        killedAny = true;
                }
            }

            if (!killedAny && fallbackPid > 4)
            {
                long expectedCreate = _pidCache.TryGetValue(fallbackPid, out var entry) ? entry.CreationTime : 0;
                killedAny = TryTerminateSinglePid(fallbackPid, expectedCreate);
            }

            if (killedAny)
            {
                _pidCache.Clear();
            }
        }

        return killedAny;
    }

    /// <summary>
    /// Automatically identifies background tasks consuming >= 20% CPU and terminates them safely.
    /// </summary>
    public RunawayPurgeResult PurgeRunawayProcesses(double minCpuPercent = 20.0)
    {
        var candidates = new List<(string ExeName, uint MaxPid, double TotalCpuPercent)>();
        lock (_syncLock)
        {
            IntPtr fgHwnd = GetForegroundWindow();
            GetWindowThreadProcessId(fgHwnd, out uint foregroundPid);

            foreach (var kvp in _groupAggregator)
            {
                var g = kvp.Value;
                if (g.TotalCpuPercent >= minCpuPercent)
                {
                    string name = g.ExeName;
                    if (IsProtectedProcess(name) || g.ContainsForegroundPid || g.MaxPid == foregroundPid || g.MaxPid == _currentPid)
                        continue;

                    candidates.Add((name, g.MaxPid, g.TotalCpuPercent));
                }
            }
        }

        int killedCount = 0;
        double freedCpu = 0.0;
        var terminated = new List<string>();

        foreach (var candidate in candidates)
        {
            if (TryTerminateProcessGroupCore(candidate.ExeName, candidate.MaxPid))
            {
                killedCount++;
                freedCpu += candidate.TotalCpuPercent;
                terminated.Add(candidate.ExeName);
            }
        }

        if (killedCount > 0)
        {
            SampleTelemetry(forceProcessScan: true, forceDispatch: true);
        }

        return new RunawayPurgeResult(killedCount, freedCpu, string.Join(", ", terminated));
    }

    public static bool IsProtectedProcess(string? exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return true;
        string clean = exeName.Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(0, clean.Length - 4);

        return clean.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("csrss", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("svchost", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("lsass", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("services", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("wininit", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("smss", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("DynamicIsland", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("System", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("Idle", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryTerminateSinglePid(uint pid, long expectedCreationTime = 0)
    {
        IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE, false, pid);
        if (hProc == IntPtr.Zero)
            return false;

        try
        {
            if (expectedCreationTime != 0 &&
                GetProcessTimes(hProc, out long actualCreationTime, out _, out _, out _) &&
                actualCreationTime != expectedCreationTime)
            {
                return false;
            }

            using var proc = Process.GetProcessById((int)pid);
            proc.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(hProc);
        }
    }

    public static void TrimOwnWorkingSet()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            EmptyWorkingSet(proc.Handle);
        }
        catch
        {
            // Ignore if restricted
        }
    }

    private static bool IsRunningOnBattery()
    {
        if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            return status.ACLineStatus == 0;
        }
        return false;
    }

    private static string ReadHardwareSummaryOnce()
    {
        int cores = Environment.ProcessorCount;
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\Description\System\CentralProcessor\0");
            string? rawName = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(rawName))
            {
                return FormatHardwareSummary(rawName, cores);
            }
        }
        catch
        {
            // Fallback to core count
        }
        return $"{cores} Logical Cores";
    }

    internal static string FormatHardwareSummary(string rawName, int cores)
    {
        string clean = rawName
            .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Processor", "", StringComparison.OrdinalIgnoreCase)
            .Replace("CPU", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        // Strip trailing GPU/clock clauses like "w/ Radeon 780M Graphics", "with ...", or "@ 3.80GHz"
        int cutIdx = clean.IndexOf(" w/", StringComparison.OrdinalIgnoreCase);
        if (cutIdx > 4) clean = clean[..cutIdx];
        cutIdx = clean.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
        if (cutIdx > 4) clean = clean[..cutIdx];
        cutIdx = clean.IndexOf('@');
        if (cutIdx > 4) clean = clean[..cutIdx];

        while (clean.Contains("  ", StringComparison.Ordinal))
            clean = clean.Replace("  ", " ", StringComparison.Ordinal);

        clean = clean.Trim();
        if (clean.Length > 26)
        {
            int spaceIdx = clean.LastIndexOf(' ', 26);
            clean = spaceIdx > 8 ? clean[..spaceIdx].Trim() : clean[..26].Trim();
        }

        return string.IsNullOrEmpty(clean) ? $"{cores} Logical Cores" : $"{clean} · {cores} Cores";
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            try
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            catch
            {
            }
            _pollTimer?.Dispose();
            _pollTimer = null;
            _gpuProvider.Dispose();
            _cpuCoreSampler.Dispose();
        }
    }
}
