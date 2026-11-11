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

                    HudCpuBarFill.Width = Math.Clamp((snap.CpuPercent / 100.0) * 190.0, 4.0, 190.0);

                    HudCpuBarFill.Background = snap.CpuPercent >= 90 ? ThemeBrushes.Red : (snap.CpuPercent >= 80 ? ThemeBrushes.Amber : ThemeBrushes.Green);

                }

                if (HudCpuStatusText != null)

                {

                    HudCpuStatusText.Text = snap.CpuPercent >= 80

                        ? "High Processor Load • Throttling Possible"

                        : "System & User Delta • Healthy";

                }

                // Memory Metrics

                if (HudRamHeroText != null) HudRamHeroText.Text = $"{snap.RamUsedGb:0.0} / {snap.RamTotalGb:0.0} GB";

                if (HudRamBarFill != null)

                {

                    HudRamBarFill.Width = Math.Clamp((snap.RamPercent / 100.0) * 190.0, 4.0, 190.0);

                    HudRamBarFill.Background = snap.RamPercent >= 92 ? ThemeBrushes.Red : (snap.RamPercent >= 85 ? ThemeBrushes.Amber : ThemeBrushes.Blue);

                }

                if (HudRamStatusText != null)

                {

                    HudRamStatusText.Text = $"{snap.RamPercent}% Load • {snap.RamAvailGb:0.0} GB Available";

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
