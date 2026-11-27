using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.System.Power;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Core;

/// <summary>
/// Operational power states governed by AC/DC power source and Windows Battery Saver.
/// </summary>
public enum PowerGovernorMode
{
    HighPerformance, // Continuous AC Power (full fidelity 60-240Hz, 1x polling)
    Balanced,        // Battery Power (balanced polling)
    BatterySaver     // Windows Battery Saver active or critical charge (throttled polling, suspended non-essentials)
}

/// <summary>
/// Dictates how a registered task behaves when the device shifts to battery or battery saver.
/// </summary>
public enum PowerSensitivity
{
    Normal,    // Runs on standard schedule across all power modes
    Relaxed,   // Relaxes interval (2x-3x) on battery/battery saver
    Suspended  // Pauses completely when Battery Saver is active
}

/// <summary>
/// Descriptor for a periodic background chore managed by the Choreographer.
/// </summary>
public sealed class ScheduledChore
{
    public string Id { get; init; } = "";
    public Action Callback { get; init; } = () => { };
    public int BaseIntervalSeconds { get; init; } = 1;
    public PowerSensitivity Sensitivity { get; init; } = PowerSensitivity.Normal;

    internal int SecondsRemaining { get; set; }

    public ScheduledChore(string id, Action callback, int baseIntervalSeconds, PowerSensitivity sensitivity = PowerSensitivity.Normal)
    {
        Id = id;
        Callback = callback;
        BaseIntervalSeconds = Math.Max(1, baseIntervalSeconds);
        Sensitivity = sensitivity;
        SecondsRemaining = BaseIntervalSeconds;
    }
}

/// <summary>
/// Centralized Power-Aware Choreographer.
/// Consolidates disparate background polling timers into a unified, aligned tick scheduler.
/// Monitors Windows Power status, throttles intervals on battery/battery saver, enforces
/// integrated GPU affinity to avoid discrete GPU wake-ups, and guarantees zero-idle CPU overhead.
/// </summary>
public sealed class Choreographer : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _masterTimer;
    private readonly Dictionary<string, ScheduledChore> _chores = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ScheduledChore> _dueBuffer = new();
    private readonly object _lock = new();

    private PowerGovernorMode _currentMode = PowerGovernorMode.HighPerformance;
    private int _secondsSinceLastPowerCheck = 0;
    private bool _suspended;
    private bool _disposed;

    public bool IsSuspended
    {
        get { lock (_lock) return _suspended; }
    }

    public IReadOnlyList<string> GetRegisteredChoreIds()
    {
        lock (_lock) return _chores.Keys.ToArray();
    }

    public PowerGovernorMode CurrentMode
    {
        get
        {
            lock (_lock) return _currentMode;
        }
        private set
        {
            PowerGovernorMode prev;
            lock (_lock)
            {
                if (_currentMode == value) return;
                prev = _currentMode;
                _currentMode = value;
                foreach (var chore in _chores.Values)
                {
                    int eff = ComputeEffectiveInterval(chore, value);
                    if (eff > 0 && (chore.SecondsRemaining <= 0 || chore.SecondsRemaining > eff))
                    {
                        chore.SecondsRemaining = eff;
                    }
                }
            }
            PowerModeChanged?.Invoke(prev, value);
        }
    }

    /// <summary>Fires when power state transitions between HighPerformance, Balanced, and BatterySaver.</summary>
    public event Action<PowerGovernorMode, PowerGovernorMode>? PowerModeChanged;

    /// <summary>Whether heavy visual effects (e.g. WASAPI loopback FFT, live backlight halo) should be throttled.</summary>
    public bool ShouldThrottleVisuals => CurrentMode == PowerGovernorMode.BatterySaver;

    public Choreographer(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _masterTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), _dispatcher);

        // Initial power mode assessment
        AssessPowerState();

        // Wire WinRT PowerManager events when available
        try
        {
            if (PowerManager.BatteryStatus != BatteryStatus.NotPresent)
            {
                PowerManager.PowerSupplyStatusChanged += OnWinRTPowerChanged;
                PowerManager.EnergySaverStatusChanged += OnWinRTPowerChanged;
            }
        }
        catch { }

        // Master timer starts on demand only when chores are registered and not suspended
    }

    public void SetSuspended(bool suspended)
    {
        lock (_lock)
        {
            if (_disposed || _suspended == suspended) return;
            _suspended = suspended;
            UpdateTimerStateLocked();
        }
    }

    private void UpdateTimerStateLocked()
    {
        bool shouldRun = !_disposed && !_suspended && _chores.Count > 0;
        if (_dispatcher.CheckAccess())
        {
            if (shouldRun && !_masterTimer.IsEnabled)
                _masterTimer.Start();
            else if (!shouldRun && _masterTimer.IsEnabled)
                _masterTimer.Stop();
        }
        else
        {
            _dispatcher.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    bool runNow = !_disposed && !_suspended && _chores.Count > 0;
                    if (runNow && !_masterTimer.IsEnabled)
                        _masterTimer.Start();
                    else if (!runNow && _masterTimer.IsEnabled)
                        _masterTimer.Stop();
                }
            }, DispatcherPriority.Background);
        }
    }

    private void OnWinRTPowerChanged(object? sender, object e)
    {
        if (_disposed) return;
        _dispatcher.InvokeAsync(AssessPowerState, DispatcherPriority.Background);
    }

    /// <summary>
    /// Evaluates current AC/DC power source and battery saver state.
    /// </summary>
    public void AssessPowerState()
    {
        if (_disposed) return;

        bool isAc = true;
        bool isBatterySaver = false;

        try
        {
            if (PowerManager.BatteryStatus != BatteryStatus.NotPresent)
            {
                isAc = PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate;
                isBatterySaver = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
            }
            else if (GetSystemPowerStatus(out var status))
            {
                isAc = status.ACLineStatus == 1;
                // Flag 128 = No system battery, 1 = High, 2 = Low, 4 = Critical
                if (!isAc && (status.BatteryFlag & 4) != 0)
                {
                    isBatterySaver = true;
                }
            }
        }
        catch
        {
            // Fallback to Win32
            if (GetSystemPowerStatus(out var status))
            {
                isAc = status.ACLineStatus == 1;
            }
        }

        PowerGovernorMode targetMode;
        if (isBatterySaver)
        {
            targetMode = PowerGovernorMode.BatterySaver;
        }
        else if (!isAc)
        {
            targetMode = PowerGovernorMode.Balanced;
        }
        else
        {
            targetMode = PowerGovernorMode.HighPerformance;
        }

        CurrentMode = targetMode;
    }

    /// <summary>
    /// Computes the effective interval (in seconds) for a chore under the active power governor mode.
    /// Returns -1 if the chore is suspended.
    /// </summary>
    public int GetEffectiveInterval(ScheduledChore chore) =>
        ComputeEffectiveInterval(chore, CurrentMode);

    private static int ComputeEffectiveInterval(ScheduledChore chore, PowerGovernorMode mode)
    {
        switch (mode)
        {
            case PowerGovernorMode.HighPerformance:
                return chore.BaseIntervalSeconds;

            case PowerGovernorMode.Balanced:
                return chore.Sensitivity switch
                {
                    PowerSensitivity.Relaxed => Math.Max(chore.BaseIntervalSeconds * 2, 2),
                    _ => chore.BaseIntervalSeconds
                };

            case PowerGovernorMode.BatterySaver:
                return chore.Sensitivity switch
                {
                    PowerSensitivity.Suspended => -1,
                    PowerSensitivity.Relaxed => Math.Max(chore.BaseIntervalSeconds * 3, 5),
                    _ => Math.Max((int)(chore.BaseIntervalSeconds * 1.5), chore.BaseIntervalSeconds + 1)
                };

            default:
                return chore.BaseIntervalSeconds;
        }
    }

    /// <summary>
    /// Registers or updates a scheduled chore.
    /// </summary>
    public void Register(string id, Action callback, int intervalSeconds, PowerSensitivity sensitivity = PowerSensitivity.Normal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(callback);

        lock (_lock)
        {
            var chore = new ScheduledChore(id, callback, intervalSeconds, sensitivity);
            chore.SecondsRemaining = ComputeEffectiveInterval(chore, _currentMode);
            _chores[id] = chore;
            UpdateTimerStateLocked();
        }
    }

    /// <summary>
    /// Unregisters an existing chore by identifier.
    /// </summary>
    public bool Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_lock)
        {
            bool removed = _chores.Remove(id);
            if (removed) UpdateTimerStateLocked();
            return removed;
        }
    }

    /// <summary>
    /// Manually triggers a chore immediately and resets its countdown.
    /// </summary>
    public bool Trigger(string id)
    {
        ScheduledChore? chore;
        lock (_lock)
        {
            if (!_chores.TryGetValue(id, out chore)) return false;
        }

        try
        {
            chore.Callback();
            lock (_lock)
            {
                int next = ComputeEffectiveInterval(chore, _currentMode);
                chore.SecondsRemaining = next > 0 ? next : chore.BaseIntervalSeconds;
            }
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[Choreographer] Manual trigger failed for '{id}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Master single-timer heartbeat tick.
    /// </summary>
    public void Tick()
    {
        if (_disposed) return;
        lock (_lock)
        {
            if (_disposed || _suspended) return;
        }

        // Check power status periodically (every 10 seconds)
        if (++_secondsSinceLastPowerCheck >= 10)
        {
            _secondsSinceLastPowerCheck = 0;
            AssessPowerState();
        }

        ScheduledChore[]? due = null;

        lock (_lock)
        {
            if (_disposed || _suspended) return;
            _dueBuffer.Clear();
            foreach (var chore in _chores.Values)
            {
                int effective = ComputeEffectiveInterval(chore, _currentMode);
                if (effective <= 0)
                {
                    // Suspended under current power mode
                    continue;
                }

                chore.SecondsRemaining--;
                if (chore.SecondsRemaining <= 0)
                {
                    chore.SecondsRemaining = effective;
                    _dueBuffer.Add(chore);
                }
            }

            if (_dueBuffer.Count > 0)
            {
                due = _dueBuffer.ToArray();
                _dueBuffer.Clear();
            }
        }

        if (due == null) return;

        foreach (var chore in due)
        {
            try
            {
                chore.Callback();
            }
            catch (Exception ex)
            {
                App.Log($"[Choreographer] Chore '{chore.Id}' threw exception: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Configures Windows DirectX Graphics Preferences for the running application executable.
    /// Enforces GpuPreference=1 (Power saving / Integrated GPU), ensuring hybrid laptop systems
    /// do not inadvertently activate discrete NVIDIA or AMD GPUs, saving 10W-25W of power.
    /// </summary>
    public static bool EnsureIntegratedGpuPreference()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            const string D3DKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
            using var key = Registry.CurrentUser.CreateSubKey(D3DKeyPath, writable: true);
            if (key == null) return false;

            string existing = (key.GetValue(exe) as string) ?? "";
            // GpuPreference=1 signifies Integrated GPU (Power saving)
            const string TargetPreference = "GpuPreference=1;AutoDirectXVersion=1;";
            if (!string.Equals(existing, TargetPreference, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(exe, TargetPreference, RegistryValueKind.String);
                App.Log($"[Choreographer] Integrated GPU preference configured for: {exe}");
            }
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[Choreographer] EnsureIntegratedGpuPreference warning: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _masterTimer.Stop();
            PowerManager.PowerSupplyStatusChanged -= OnWinRTPowerChanged;
            PowerManager.EnergySaverStatusChanged -= OnWinRTPowerChanged;
        }
        catch { }

        lock (_lock)
        {
            _chores.Clear();
        }
    }
}
