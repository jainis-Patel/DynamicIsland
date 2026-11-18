using System;

using System.Diagnostics;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Input;

using DynamicIsland.Core;

using DynamicIsland.Services;

namespace DynamicIsland;

public partial class IslandWindow

{

    // ═════════════════════════════════════════════════════════════════

    //  Activity & System Performance HUD (Zero-Allocation Engine)

    // ═════════════════════════════════════════════════════════════════

    private bool _isHighLoadAlertActive;

    private void OnSystemVitalsUpdated(SystemVitalsSnapshot snap)

    {

        Dispatcher.InvokeAsync(() =>

        {

            // 1. Live CPU % badge on the Dock button & Clock header telemetry badges
            string cpuStr = $"CPU {snap.CpuPercent}%";
            string ramStr = $"RAM {snap.RamPercent}%";
            if (CpuText != null) CpuText.Text = cpuStr;
            if (RamText != null) RamText.Text = ramStr;
            if (ClockCpuText != null) ClockCpuText.Text = cpuStr;
            if (ClockRamText != null) ClockRamText.Text = ramStr;

            if (SystemHudBadgeText != null)
            {
                SystemHudBadgeText.Text = $"{snap.CpuPercent}%";
                SystemHudBadgeText.Foreground = snap.CpuPercent >= 80 ? ThemeBrushes.Amber : ThemeBrushes.PrimaryText;
            }

            // 2. Idle Ambient High-Load Alert Pill

            bool shouldAlert = _machine.State != IslandState.Expanded && (snap.IsHighCpu || snap.IsHighRam);

            bool alertStateChanged = shouldAlert != _isHighLoadAlertActive;

            _isHighLoadAlertActive = shouldAlert;

            if (SystemHudAlertPill != null)

            {

                SystemHudAlertPill.Visibility = shouldAlert ? Visibility.Visible : Visibility.Collapsed;

                if (shouldAlert && SystemHudAlertText != null)

                {

                    // Prioritize RAM if RAM is high and greater than or equal to CPU load, or if only RAM is high

                    bool isRamPriority = snap.IsHighRam && (!snap.IsHighCpu || snap.RamPercent >= snap.CpuPercent);

                    if (isRamPriority)

                    {

                        SystemHudAlertText.Text = $"⚠️ {snap.RamPercent}% RAM";

                        SystemHudAlertText.Foreground = ThemeBrushes.Red;

                        SystemHudAlertPill.Background = ThemeBrushes.AlertRedBg;

                        SystemHudAlertPill.BorderBrush = ThemeBrushes.AlertRedBorder;

                        SystemHudAlertPill.ToolTip = $"⚠️ High Memory Load ({snap.RamPercent}% • {snap.RamUsedGb:0.0} / {snap.RamTotalGb:0.0} GB)\nClick to open System HUD & Purge Standby RAM";

                    }

                    else

                    {

                        bool isCriticalCpu = snap.CpuPercent >= 90;

                        SystemHudAlertText.Text = $"🔥 {snap.CpuPercent}% CPU";

                        SystemHudAlertText.Foreground = isCriticalCpu ? ThemeBrushes.Red : ThemeBrushes.Amber;

                        SystemHudAlertPill.Background = isCriticalCpu ? ThemeBrushes.AlertRedBg : ThemeBrushes.AlertAmberBg;

                        SystemHudAlertPill.BorderBrush = isCriticalCpu ? ThemeBrushes.AlertRedBorder : ThemeBrushes.AlertAmberBorder;

                        string hogHint = snap.HogCount > 0 ? $"\nTop Process: {snap.Hog1.DisplayName} ({snap.Hog1.StatsText})" : string.Empty;

                        SystemHudAlertPill.ToolTip = $"🔥 High Processor Load ({snap.CpuPercent}%){hogHint}\nClick to open System HUD & inspect processes";

                    }

                }

            }

            // Smooth spring animation if alert toggles in Idle state
            if (alertStateChanged && _machine.State == IslandState.Idle)
            {
                RefreshTargetGeometry();
                if (shouldAlert && !IsReducedMotion)
                {
                    TriggerLiquidImpulse(35, -12, 6);
                }
            }

            // 3. Render Expanded HUD card elements when expanded or visible

            if (SystemHudContainer != null && (SystemHudContainer.Visibility == Visibility.Visible || _currentExpandedView == ExpandedView.SystemHud))
            {
                if (HudHardwareSubtitle != null && _systemPerfService != null && !string.IsNullOrWhiteSpace(_systemPerfService.HardwareSummary))
                {
                    HudHardwareSubtitle.Text = _systemPerfService.HardwareSummary;
                }

                // Processor Metrics
                if (HudCpuHeroText != null) HudCpuHeroText.Text = $"{snap.CpuPercent}%";
                if (HudCpuBarFill != null)
                {
                    HudCpuBarFill.Width = Math.Clamp((snap.CpuPercent / 100.0) * 110.0, 3.0, 110.0);
                    HudCpuBarFill.Background = snap.CpuPercent >= 90 ? ThemeBrushes.Red : (snap.CpuPercent >= 80 ? ThemeBrushes.Amber : ThemeBrushes.Green);
                }
                if (HudCpuStatusText != null)
                {
                    HudCpuStatusText.Text = snap.CpuPercent >= 80
                        ? "High Load • Throttling"
                        : $"{Environment.ProcessorCount} Cores • Healthy";
                }

                // Per-Core Heatmap Rendering
                if (HudCpuHeatmapPanel != null && snap.Cores.CoreLoads != null && snap.Cores.CoreLoads.Length > 0)
                {
                    int coreCount = snap.Cores.CoreLoads.Length;
                    if (HudCpuHeatmapPanel.Children.Count != coreCount)
                    {
                        HudCpuHeatmapPanel.Children.Clear();
                        double barWidth = Math.Max(2.0, (105.0 / coreCount) - 1.0);
                        for (int i = 0; i < coreCount; i++)
                        {
                            var b = new System.Windows.Controls.Border
                            {
                                Width = barWidth,
                                Height = 3,
                                CornerRadius = new CornerRadius(1),
                                Margin = new Thickness(0, 0, 1, 0),
                                Background = ThemeBrushes.Green
                            };
                            HudCpuHeatmapPanel.Children.Add(b);
                        }
                    }

                    for (int i = 0; i < coreCount && i < HudCpuHeatmapPanel.Children.Count; i++)
                    {
                        if (HudCpuHeatmapPanel.Children[i] is System.Windows.Controls.Border b)
                        {
                            int load = snap.Cores.CoreLoads[i];
                            b.Background = load >= 85 ? ThemeBrushes.Red : (load >= 60 ? ThemeBrushes.Amber : ThemeBrushes.Green);
                        }
                    }
                }

                // GPU Metrics
                if (HudGpuHeroText != null)
                {
                    if (snap.Gpu.IsAvailable)
                    {
                        HudGpuHeroText.Text = snap.Gpu.UtilizationPercent > 0
                            ? $"{snap.Gpu.UtilizationPercent}%"
                            : (snap.Gpu.TemperatureC > 0 ? $"{snap.Gpu.TemperatureC}°C" : $"{snap.Gpu.VramPercent}%");
                    }
                    else
                    {
                        HudGpuHeroText.Text = "0%";
                    }
                }

                if (HudGpuBarFill != null)
                {
                    double gpuRatio = snap.Gpu.IsAvailable
                        ? Math.Max(snap.Gpu.UtilizationPercent, snap.Gpu.VramPercent) / 100.0
                        : 0.0;
                    HudGpuBarFill.Width = Math.Clamp(gpuRatio * 110.0, 3.0, 110.0);
                    HudGpuBarFill.Background = snap.Gpu.TemperatureC >= 85 ? ThemeBrushes.Red : ThemeBrushes.Purple;
                }

                if (HudGpuStatusText != null)
                {
                    if (snap.Gpu.IsAvailable)
                    {
                        string tempStr = snap.Gpu.TemperatureC > 0 ? $"{snap.Gpu.TemperatureC}°C • " : "";
                        HudGpuStatusText.Text = $"{tempStr}{snap.Gpu.VramUsedGb:0.0}/{snap.Gpu.VramTotalGb:0.0} GB";
                    }
                    else
                    {
                        HudGpuStatusText.Text = "Discrete GPU Sleep";
                    }
                }

                // Memory Metrics
                if (HudRamHeroText != null) HudRamHeroText.Text = $"{snap.RamUsedGb:0.0} GB";
                if (HudRamBarFill != null)
                {
                    HudRamBarFill.Width = Math.Clamp((snap.RamPercent / 100.0) * 110.0, 3.0, 110.0);
                    HudRamBarFill.Background = snap.RamPercent >= 92 ? ThemeBrushes.Red : (snap.RamPercent >= 85 ? ThemeBrushes.Amber : ThemeBrushes.Blue);
                }
                if (HudRamStatusText != null)
                {
                    HudRamStatusText.Text = $"{snap.RamPercent}% • {snap.RamAvailGb:0.0} GB Free";
                }

                // Runaway Process Banner
                if (HudRunawayBanner != null)
                {
                    HudRunawayBanner.Visibility = snap.Runaway.HasRunaway ? Visibility.Visible : Visibility.Collapsed;
                    if (snap.Runaway.HasRunaway && HudRunawayText != null)
                    {
                        HudRunawayText.Text = $"Runaway: {snap.Runaway.ExeName} ({snap.Runaway.CpuPercent:0.0}% CPU)";
                    }
                }

                // Hardware & System Health Chip

                if (HudSystemHealthChip != null)

                {

                    if (snap.IsHighCpu || snap.IsHighRam)

                    {

                        HudSystemHealthChip.Text = "Heavy Load";

                        HudSystemHealthChip.Foreground = snap.CpuPercent >= 90 ? ThemeBrushes.Red : ThemeBrushes.Amber;

                    }

                    else

                    {

                        HudSystemHealthChip.Text = "Optimal";

                        HudSystemHealthChip.Foreground = ThemeBrushes.Green;

                    }

                }

                // Top Resource Hogs (Grouped Process Tree Aggregates)

                if (HudHogRow1 != null)

                {

                    HudHogRow1.Visibility = snap.HogCount >= 1 ? Visibility.Visible : Visibility.Collapsed;

                    if (snap.HogCount >= 1)

                    {

                        if (HudHog1Name != null) HudHog1Name.Text = snap.Hog1.DisplayName;

                        if (HudHog1Stats != null) HudHog1Stats.Text = snap.Hog1.StatsText;

                    }

                }

                if (HudHogRow2 != null)

                {

                    HudHogRow2.Visibility = snap.HogCount >= 2 ? Visibility.Visible : Visibility.Collapsed;

                    if (snap.HogCount >= 2)

                    {

                        if (HudHog2Name != null) HudHog2Name.Text = snap.Hog2.DisplayName;

                        if (HudHog2Stats != null) HudHog2Stats.Text = snap.Hog2.StatsText;

                    }

                }

                if (HudHogRow3 != null)

                {

                    HudHogRow3.Visibility = snap.HogCount >= 3 ? Visibility.Visible : Visibility.Collapsed;

                    if (snap.HogCount >= 3)

                    {

                        if (HudHog3Name != null) HudHog3Name.Text = snap.Hog3.DisplayName;

                        if (HudHog3Stats != null) HudHog3Stats.Text = snap.Hog3.StatsText;

                    }

                }

            }

        });

    }

    private void SystemHudAlertPill_Click(object sender, MouseButtonEventArgs e)

    {

        e.Handled = true;

        SwitchExpandedView(ExpandedView.SystemHud);

    }

    internal void HudPurgeRamButton_Click(object sender, RoutedEventArgs e)

    {

        if (_systemPerfService == null) return;

        Task.Run(() =>

        {

            var result = _systemPerfService.PurgeIdleRam();

            Dispatcher.InvokeAsync(() =>

            {

                TriggerLiquidImpulse(55, -20, 8);

                if (result.OnCooldown)

                {

                    if (HudPurgeRamText != null) HudPurgeRamText.Text = "⏳ Cooldown (10s)";

                    _machine.PushToast(new ToastContent(

                        "\uE7BA",

                        "RAM Purge on Cooldown",

                        "Wait 10 seconds between trims to prevent working set trashing.",

                        ThemeBrushes.AmberColor,

                        TimeSpan.FromSeconds(2.0),

                        TargetView: "systemhud",

                        Priority: ToastPriority.Feedback,

                        Category: "system"));

                }

                else

                {

                    if (HudPurgeRamText != null) HudPurgeRamText.Text = $"✨ Purged {result.FreedMb:N0} MB";

                    _machine.PushToast(new ToastContent(

                        "\uE777",

                        "Idle Memory Purged",

                        $"Safely trimmed {result.FreedMb:N0} MB of idle background RAM.",

                        ThemeBrushes.GreenColor,

                        TimeSpan.FromSeconds(2.5),

                        TargetView: "systemhud",

                        Priority: ToastPriority.Feedback,

                        Category: "system"));

                }

            });

        });

    }

    private int _armedKillRank;
    private string? _armedKillExe;
    private DateTimeOffset _armedKillExpiry;

    internal void HudKillHog1_Click(object sender, RoutedEventArgs e) => TerminateHogByRank(1);

    internal void HudKillHog2_Click(object sender, RoutedEventArgs e) => TerminateHogByRank(2);

    internal void HudKillHog3_Click(object sender, RoutedEventArgs e) => TerminateHogByRank(3);

    private void ResetKillButtonsUI()
    {
        _armedKillRank = 0;
        _armedKillExe = null;
        if (HudHog1KillText != null) HudHog1KillText.Text = "\u2715 Kill";
        if (HudHog2KillText != null) HudHog2KillText.Text = "\u2715 Kill";
        if (HudHog3KillText != null) HudHog3KillText.Text = "\u2715 Kill";
    }

    private void TerminateHogByRank(int rank)
    {
        if (_systemPerfService == null) return;

        var snap = _systemPerfService.LatestSnapshot;
        ProcessHogInfo hog = rank switch
        {
            1 => snap.Hog1,
            2 => snap.Hog2,
            3 => snap.Hog3,
            _ => default
        };

        if (string.IsNullOrWhiteSpace(hog.ExeName)) return;

        string procName = hog.ExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? hog.ExeName.Substring(0, hog.ExeName.Length - 4)
            : hog.ExeName;

        // Skip critical OS processes
        if (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
            procName.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
            procName.Equals("csrss", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool isConfirmed = shiftHeld ||
                           (_armedKillRank == rank &&
                            string.Equals(_armedKillExe, hog.ExeName, StringComparison.OrdinalIgnoreCase) &&
                            DateTimeOffset.UtcNow <= _armedKillExpiry);

        if (!isConfirmed)
        {
            ResetKillButtonsUI();
            _armedKillRank = rank;
            _armedKillExe = hog.ExeName;
            _armedKillExpiry = DateTimeOffset.UtcNow.AddSeconds(3.0);

            var targetLabel = rank switch
            {
                1 => HudHog1KillText,
                2 => HudHog2KillText,
                3 => HudHog3KillText,
                _ => null
            };
            if (targetLabel != null) targetLabel.Text = "Confirm?";

            _ = Task.Delay(3100).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                if (_armedKillRank == rank && DateTimeOffset.UtcNow > _armedKillExpiry)
                {
                    ResetKillButtonsUI();
                }
            }));
            return;
        }

        ResetKillButtonsUI();

        Task.Run(() =>

        {

            int killed = 0;

            try

            {

                var procs = Process.GetProcessesByName(procName);

                foreach (var p in procs)

                {

                    try

                    {

                        p.Kill();

                        killed++;

                    }

                    catch

                    {

                        // Best effort

                    }

                    finally

                    {

                        p.Dispose();

                    }

                }

            }

            catch

            {

                // Best effort

            }

            Dispatcher.InvokeAsync(() =>

            {

                TriggerLiquidImpulse(45, -15, 6);

                _machine.PushToast(new ToastContent(

                    "\uE711",

                    $"Terminated {hog.ExeName}",

                    $"Closed {killed} instance{(killed == 1 ? "" : "s")}.",

                    ThemeBrushes.AmberColor,

                    TimeSpan.FromSeconds(2.0),

                    TargetView: "systemhud",

                    Priority: ToastPriority.Feedback,

                    Category: "system"));

            });

        });

    }

    internal void HudKillRunaway_Click(object sender, RoutedEventArgs e)
    {
        if (_systemPerfService == null) return;
        var snap = _systemPerfService.LatestSnapshot;
        if (!snap.Runaway.HasRunaway || string.IsNullOrEmpty(snap.Runaway.ExeName)) return;

        string runawayExe = snap.Runaway.ExeName;
        uint runawayPid = snap.Runaway.Pid;

        Task.Run(() =>
        {
            bool killed = _systemPerfService.TryTerminateProcessGroup(runawayExe, runawayPid);
            Dispatcher.InvokeAsync(() =>
            {
                TriggerLiquidImpulse(45, -15, 6);
                if (killed)
                {
                    _machine.PushToast(new ToastContent(
                        "\uE711",
                        "Terminated Runaway Task",
                        $"Closed runaway background process {runawayExe}.",
                        ThemeBrushes.AmberColor,
                        TimeSpan.FromSeconds(2.0),
                        TargetView: "systemhud",
                        Priority: ToastPriority.Feedback,
                        Category: "system"));
                }
            });
        });
    }

    internal void HudPurgeRunawaysButton_Click(object sender, RoutedEventArgs e)
    {
        if (_systemPerfService == null) return;

        Task.Run(() =>
        {
            var result = _systemPerfService.PurgeRunawayProcesses(20.0);
            Dispatcher.InvokeAsync(() =>
            {
                TriggerLiquidImpulse(55, -20, 8);
                if (result.KilledProcesses > 0)
                {
                    if (HudPurgeRunawaysText != null)
                        HudPurgeRunawaysText.Text = $"⚡ Purged ({result.KilledProcesses})";

                    _machine.PushToast(new ToastContent(
                        "\uE777",
                        "Runaway Tasks Purged",
                        $"Safely terminated {result.KilledProcesses} runaway task(s), freeing ~{result.FreedCpuPercent:0.0}% CPU.",
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(2.5),
                        TargetView: "systemhud",
                        Priority: ToastPriority.Feedback,
                        Category: "system"));
                }
                else
                {
                    if (HudPurgeRunawaysText != null)
                        HudPurgeRunawaysText.Text = "✓ All Clear";

                    _machine.PushToast(new ToastContent(
                        "\uE73E",
                        "No Runaways Detected",
                        "All background tasks are operating within normal CPU thresholds.",
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(2.0),
                        TargetView: "systemhud",
                        Priority: ToastPriority.Feedback,
                        Category: "system"));
                }
            });
        });
    }

    internal void HudTaskManagerButton_Click(object sender, RoutedEventArgs e)

    {

        try

        {

            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });

        }

        catch

        {

            // Ignore if canceled or blocked by policy

        }

    }

}
