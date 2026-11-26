using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    // =================================================================
    //  Smart Battery & Power Management Widget
    // =================================================================

    private void OnPowerStatusChanged(PowerStatusInfo status)
    {
        if (BatteryBadgeText == null) return;

        BatteryBadgeText.Text = status.HasBattery ? $"{status.BatteryPercent}%" : "AC";
        BatteryButtonGlyph.Text = status.BatteryGlyph;

        var dpHealthText = SettingsContainer?.DisplayPowerPanel?.BatteryHealthSummaryText;
        if (dpHealthText != null)
        {
            dpHealthText.Text = status.HasBattery
                ? $"Health: {status.HealthPercent}% \u2022 {(status.IsCharging ? "Charging" : (status.IsAcOnline ? "AC" : "Batt"))}"
                : "Continuous AC Power";
        }

        if (_batteryContainer == null) return;

        BatteryHeroPercentText.Text = $"{status.BatteryPercent}%";
        BatteryStatusSubtitle.Text = status.StatusSubtitle;
        BatteryTimeEstimate.Text = status.TimeEstimateText;
        BatteryChargingBolt.Visibility = status.IsCharging || !status.HasBattery ? Visibility.Visible : Visibility.Collapsed;

        if (BatteryWattageText != null)
        {
            BatteryWattageText.Text = status.WattageText;
            BatteryWattageText.Foreground = status.IsCharging || !status.HasBattery ? ThemeBrushes.Green : ThemeBrushes.Amber;
        }

        if (BatteryHealthText != null)
        {
            BatteryHealthText.Text = status.HasBattery && status.HealthPercent.HasValue
                ? $"Health: {status.HealthPercent}%"
                : "Continuous AC";
        }

        if (BatteryHeaderBadgeText != null)
        {
            BatteryHeaderBadgeText.Text = status.IsAcOnline
                ? (status.IsCharging ? "Charging" : (status.HasBattery ? "Connected to AC" : "Desktop AC Power"))
                : "On Battery";
        }

        if (BatteryHeaderDot != null)
        {
            BatteryHeaderDot.Fill = status.IsCharging || (status.IsAcOnline && status.BatteryPercent > 20)
                ? ThemeBrushes.Green
                : status.BatteryPercent > 20
                    ? ThemeBrushes.Blue
                    : ThemeBrushes.Amber;
        }

        if (BatteryWattageBadge != null)
        {
            BatteryWattageBadge.Background = status.IsCharging || !status.HasBattery
                ? ThemeBrushes.PlanSaverActiveBg
                : ThemeBrushes.PlanPerfActiveBg;
            BatteryWattageBadge.BorderBrush = status.IsCharging || !status.HasBattery
                ? ThemeBrushes.GreenBorderActive
                : ThemeBrushes.AmberBorderActive;
        }

        double pct = Math.Clamp(status.BatteryPercent / 100.0, 0.05, 1.0);
        if (BatteryLevelFill != null)
        {
            BatteryLevelFill.Width = Math.Clamp(pct * 18.0, 2.0, 18.0);
            BatteryLevelFill.Background = status.IsCharging || status.BatteryPercent > 30
                ? ThemeBrushes.Green
                : status.BatteryPercent > 15
                    ? ThemeBrushes.Amber
                    : ThemeBrushes.Red;
        }

        UpdatePowerPlanCardsUI(status.ActiveSchemeKey);
        UpdateDisplayTopologyBadges();
    }

    private void UpdateDisplayTopologyBadges()
    {
        if (DisplayRefreshText == null && DisplayResolutionText == null) return;
        try
        {
            var topo = DisplayTopologyService.QueryConnectedMonitors(GetTargetMonitor());
            int idx = _settingsService != null
                ? Math.Clamp(_settingsService.Current.MonitorIndex, 0, Math.Max(0, topo.Count - 1))
                : 0;

            if (idx >= 0 && idx < topo.Count)
            {
                var mon = topo[idx];
                if (DisplayRefreshText != null) DisplayRefreshText.Text = mon.RefreshRateText;
                if (DisplayResolutionText != null) DisplayResolutionText.Text = mon.ResolutionText;
            }
            else
            {
                if (DisplayRefreshText != null) DisplayRefreshText.Text = "60 Hz";
                if (DisplayResolutionText != null) DisplayResolutionText.Text = "Standard Display";
            }
        }
        catch
        {
            // Fallback gracefully
        }
    }

    private void UpdatePowerPlanCardsUI(string activeKey)
    {
        bool isPerf = activeKey == "Performance";
        bool isBalanced = activeKey == "Balanced";
        bool isSaver = activeKey == "PowerSaver";

        if (PlanPerfCard != null)
        {
            PlanPerfCard.BorderBrush = isPerf ? ThemeBrushes.PlanPerfActiveBorder : Brushes.Transparent;
            PlanPerfCard.Background = isPerf ? ThemeBrushes.PlanPerfActiveBg : Brushes.Transparent;
            if (PlanPerfTitle != null) PlanPerfTitle.Foreground = isPerf ? Brushes.White : ThemeBrushes.SecondaryText;
            if (PlanPerfIcon != null) PlanPerfIcon.Foreground = isPerf ? ThemeBrushes.Amber : ThemeBrushes.MutedGrey;
        }

        if (PlanBalancedCard != null)
        {
            PlanBalancedCard.BorderBrush = isBalanced ? ThemeBrushes.PlanBalancedActiveBorder : Brushes.Transparent;
            PlanBalancedCard.Background = isBalanced ? ThemeBrushes.PlanBalancedActiveBg : Brushes.Transparent;
            if (PlanBalancedTitle != null) PlanBalancedTitle.Foreground = isBalanced ? Brushes.White : ThemeBrushes.SecondaryText;
            if (PlanBalancedIcon != null) PlanBalancedIcon.Foreground = isBalanced ? ThemeBrushes.Blue : ThemeBrushes.MutedGrey;
        }

        if (PlanSaverCard != null)
        {
            PlanSaverCard.BorderBrush = isSaver ? ThemeBrushes.PlanSaverActiveBorder : Brushes.Transparent;
            PlanSaverCard.Background = isSaver ? ThemeBrushes.PlanSaverActiveBg : Brushes.Transparent;
            if (PlanSaverTitle != null) PlanSaverTitle.Foreground = isSaver ? Brushes.White : ThemeBrushes.SecondaryText;
            if (PlanSaverIcon != null) PlanSaverIcon.Foreground = isSaver ? ThemeBrushes.Green : ThemeBrushes.MutedGrey;
        }
    }

    private void OnChargingPulseTriggered(int percent, string timeEstimate)
    {
        TriggerLiquidImpulse(240, 85, 14);
        _machine.PushToast(new ToastContent(
            "\uE945",
            "MagSafe Fast Charging",
            $"{percent}% \u00B7 {timeEstimate}",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(3.5),
            TargetView: "battery",
            Priority: ToastPriority.System,
            Category: "power"));
        PillGlowColor.Color = ThemeBrushes.GreenColor;
        PillGlow.Opacity = 0.95;
        EnsureRendering();
    }

    private void OnPowerDisconnectedTriggered(int percent, string timeEstimate)
    {
        _machine.PushToast(new ToastContent(
            "\uEBB5",
            "On Battery",
            $"{percent}% \u00B7 {timeEstimate}",
            ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(3.0),
            TargetView: "battery",
            Priority: ToastPriority.System,
            Category: "power"));
    }

    private void OnLowBatteryTriggered(int percent)
    {
        TriggerLiquidImpulse(220, 80, 10);
        _machine.PushToast(new ToastContent(
            "\uEBA0",
            "Low Battery Warning",
            $"{percent}% remaining \u00B7 Power Saver engaged",
            ThemeBrushes.RedColor,
            TimeSpan.FromSeconds(4.5),
            TargetView: "battery",
            Priority: ToastPriority.Critical));
        PillGlowColor.Color = ThemeBrushes.RedColor;
        PillGlow.Opacity = 0.85;
        EnsureRendering();
    }

    public enum CaffeineMode
    {
        Off,
        Minutes15,
        Hour1,
        Indefinite,
        WhileOnAc,
        UntilProcessExits
    }

    private bool _isCaffeineActive;
    private CaffeineMode _caffeineMode = CaffeineMode.Off;
    private DateTime _caffeineExpiresUtc;
    private DispatcherTimer? _caffeineTimer;
    private int _caffeineTargetPid;
    private string _caffeineTargetAppName = string.Empty;

    internal bool IsCaffeineActive => _isCaffeineActive;
    internal string CaffeineShortLabel => _caffeineMode switch
    {
        CaffeineMode.Minutes15 => "Awake 15m",
        CaffeineMode.Hour1 => "Awake 1h",
        CaffeineMode.Indefinite => "Awake ∞",
        CaffeineMode.WhileOnAc => "Awake AC",
        CaffeineMode.UntilProcessExits => "Awake App",
        _ => "Awake"
    };

    internal void CaffeineQuickBtn_Click(object sender, RoutedEventArgs e)
    {
        CaffeineMode next = _caffeineMode switch
        {
            CaffeineMode.Off => CaffeineMode.Minutes15,
            CaffeineMode.Minutes15 => CaffeineMode.Hour1,
            CaffeineMode.Hour1 => CaffeineMode.Indefinite,
            CaffeineMode.Indefinite => CaffeineMode.WhileOnAc,
            _ => CaffeineMode.Off
        };
        SetCaffeineMode(next);
    }

    internal void CaffeineQuickBtn_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu();

        void AddOption(string header, CaffeineMode mode, string glyph)
        {
            var mi = new MenuItem
            {
                Header = header,
                Icon = ThemeBrushes.CreateMenuIcon(glyph),
                IsChecked = _caffeineMode == mode
            };
            mi.Click += (_, _) => SetCaffeineMode(mode);
            menu.Items.Add(mi);
        }

        AddOption("Off (Normal Sleep)", CaffeineMode.Off, "\uE708");
        AddOption("15 Minutes", CaffeineMode.Minutes15, "\uEC32");
        AddOption("1 Hour", CaffeineMode.Hour1, "\uEC32");
        AddOption("Indefinite", CaffeineMode.Indefinite, "\uEC32");
        AddOption("While on AC Power", CaffeineMode.WhileOnAc, "\uE945");

        var procItem = new MenuItem
        {
            Header = "Until Foreground App Exits",
            Icon = ThemeBrushes.CreateMenuIcon("\uE737"),
            IsChecked = _caffeineMode == CaffeineMode.UntilProcessExits
        };
        procItem.Click += (_, _) => SetCaffeineMode(CaffeineMode.UntilProcessExits);
        menu.Items.Add(procItem);

        menu.PlacementTarget = CaffeineQuickBtn;
        menu.IsOpen = true;
    }

    public void SetCaffeineMode(CaffeineMode mode, int explicitPid = 0, string? explicitProcessName = null)
    {
        _caffeineMode = mode;
        _caffeineTimer?.Stop();

        if (mode == CaffeineMode.Off)
        {
            _isCaffeineActive = false;
            ApplyCaffeineMode(false, "Awake");
            _liveActivityHub?.Complete("caffeine");
            _machine.PushToast(new ToastContent(
                "\uE706",
                "Keep Awake Disabled",
                "Normal Windows sleep schedule restored",
                ThemeBrushes.MutedGreyColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "battery",
                Priority: ToastPriority.Feedback,
                Category: "caffeine"));
            return;
        }

        _isCaffeineActive = true;
        string badgeLabel = "Awake ∞";
        string toastSub = "Display & PC sleep prevented indefinitely";

        if (mode == CaffeineMode.Minutes15)
        {
            _caffeineExpiresUtc = DateTime.UtcNow.AddMinutes(15);
            badgeLabel = "Awake 15m";
            toastSub = "Preventing sleep for 15 minutes";
            EnsureCaffeineTimerRunning();
        }
        else if (mode == CaffeineMode.Hour1)
        {
            _caffeineExpiresUtc = DateTime.UtcNow.AddHours(1);
            badgeLabel = "Awake 1h";
            toastSub = "Preventing sleep for 1 hour";
            EnsureCaffeineTimerRunning();
        }
        else if (mode == CaffeineMode.WhileOnAc)
        {
            badgeLabel = "Awake (AC)";
            toastSub = "Keeping PC awake while connected to AC power";
            EnsureCaffeineTimerRunning();
        }
        else if (mode == CaffeineMode.UntilProcessExits)
        {
            if (explicitPid > 0)
            {
                _caffeineTargetPid = explicitPid;
                _caffeineTargetAppName = explicitProcessName ?? $"PID {explicitPid}";
            }
            else
            {
                CaptureForegroundProcessForCaffeine(out _caffeineTargetPid, out _caffeineTargetAppName);
            }
            badgeLabel = $"Awake ({_caffeineTargetAppName})";
            toastSub = $"Keeping PC awake until {_caffeineTargetAppName} exits";
            EnsureCaffeineTimerRunning();
        }

        ApplyCaffeineMode(true, badgeLabel);
        PublishCaffeineLiveActivity(badgeLabel, toastSub, -1);

        _machine.PushToast(new ToastContent(
            "\uE706",
            $"Caffeine Pro · {badgeLabel}",
            toastSub,
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.2),
            TargetView: "battery",
            Priority: ToastPriority.Feedback,
            Category: "caffeine"));
    }

    private static void CaptureForegroundProcessForCaffeine(out int pid, out string appName)
    {
        pid = 0;
        appName = "Active App";
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                GetWindowThreadProcessId(fg, out uint rawPid);
                int myPid = Environment.ProcessId;
                if (rawPid != 0 && rawPid != myPid)
                {
                    pid = (int)rawPid;
                    using var p = Process.GetProcessById(pid);
                    appName = string.IsNullOrWhiteSpace(p.ProcessName) ? $"PID {pid}" : p.ProcessName;
                    return;
                }
            }
            // Fallback to first visible third-party window process
            Process[] procs = Process.GetProcesses();
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id != Environment.ProcessId && p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.ProcessName))
                        {
                            pid = p.Id;
                            appName = p.ProcessName;
                            return;
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
        catch { }
    }

    private void EnsureCaffeineTimerRunning()
    {
        if (_caffeineTimer == null)
        {
            _caffeineTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, OnCaffeineTimerTick, Dispatcher);
        }
        _caffeineTimer.Start();
    }

    private void OnCaffeineTimerTick(object? sender, EventArgs e)
    {
        if (!_isCaffeineActive || _caffeineMode == CaffeineMode.Off || _caffeineMode == CaffeineMode.Indefinite)
        {
            _caffeineTimer?.Stop();
            return;
        }

        if (_caffeineMode == CaffeineMode.Minutes15 || _caffeineMode == CaffeineMode.Hour1)
        {
            var rem = _caffeineExpiresUtc - DateTime.UtcNow;
            if (rem <= TimeSpan.Zero)
            {
                SetCaffeineMode(CaffeineMode.Off);
                return;
            }
            double totalSec = _caffeineMode == CaffeineMode.Minutes15 ? 900.0 : 3600.0;
            double progress = Math.Clamp(1.0 - (rem.TotalSeconds / totalSec), 0.0, 1.0);
            string label = rem.TotalMinutes >= 1 ? $"Awake {(int)Math.Ceiling(rem.TotalMinutes)}m" : $"Awake {rem.Seconds}s";
            if (CaffeineQuickText != null) CaffeineQuickText.Text = label;
            PublishCaffeineLiveActivity(label, $"{rem:mm\\:ss} remaining", progress);
        }
        else if (_caffeineMode == CaffeineMode.WhileOnAc)
        {
            var status = _powerPlanService.Current;
            if (!status.IsAcOnline)
            {
                SetCaffeineMode(CaffeineMode.Off);
            }
        }
        else if (_caffeineMode == CaffeineMode.UntilProcessExits)
        {
            if (_caffeineTargetPid <= 0)
            {
                SetCaffeineMode(CaffeineMode.Off);
                return;
            }
            try
            {
                using var proc = Process.GetProcessById(_caffeineTargetPid);
                if (proc.HasExited)
                {
                    SetCaffeineMode(CaffeineMode.Off);
                }
            }
            catch
            {
                SetCaffeineMode(CaffeineMode.Off);
            }
        }
    }

    private void PublishCaffeineLiveActivity(string title, string subtitle, double progress)
    {
        _liveActivityHub?.PublishOrUpdate(new LiveActivityItem
        {
            Id = "caffeine",
            Category = "caffeine",
            Glyph = "\uEC32",
            Title = title,
            Subtitle = subtitle,
            AccentColor = ThemeBrushes.GreenColor,
            Progress = progress,
            Priority = 20,
            TargetView = "battery"
        });
    }

    internal static bool ApplyCaffeineExecutionState(bool keepAwake)
    {
        try
        {
            uint flags = keepAwake
                ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)
                : ES_CONTINUOUS;
            return SetThreadExecutionState(flags) != 0;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyCaffeineMode(bool active, string badgeLabel = "Awake On")
    {
        ApplyCaffeineExecutionState(active);
        if (_batteryContainer != null && CaffeineQuickBtn != null && CaffeineQuickText != null)
        {
            if (active)
            {
                CaffeineQuickBtn.Background = ThemeBrushes.PlanSaverActiveBg;
                CaffeineQuickBtn.BorderBrush = ThemeBrushes.PlanSaverActiveBorder;
                CaffeineQuickText.Text = badgeLabel;
                CaffeineQuickText.Foreground = ThemeBrushes.Green;
                if (CaffeineQuickIcon != null) CaffeineQuickIcon.Foreground = ThemeBrushes.Green;
            }
            else
            {
                CaffeineQuickBtn.Background = ThemeBrushes.ButtonSubtleBg;
                CaffeineQuickBtn.BorderBrush = ThemeBrushes.BorderSubtle;
                CaffeineQuickText.Text = "Awake";
                CaffeineQuickText.Foreground = ThemeBrushes.SecondaryText;
                if (CaffeineQuickIcon != null) CaffeineQuickIcon.Foreground = ThemeBrushes.SecondaryText;
            }
        }
    }

    internal void NightLightQuickBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleNightLight();
    }

    internal void NightLightSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleNightLight();
    }

    private void ToggleNightLight()
    {
        if (_settingsService == null) return;
        bool next = !_settingsService.Current.EnableNightLightSim;
        _settingsService.Update(s => s.EnableNightLightSim = next, nameof(AppSettings.EnableNightLightSim));
        DisplayTopologyService.SetNightLight(next, _settingsService.Current.NightLightWarmthKelvin);
        UpdateDisplayAndBatterySettingsUI();

        _machine.PushToast(new ToastContent(
            "\uE708",
            "Night Light (Eye Comfort)",
            next ? $"Warm candle tone enabled ({_settingsService.Current.NightLightWarmthKelvin}K)" : "Night Light disabled (Natural daylight)",
            next ? ThemeBrushes.AmberColor : ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "battery",
            Priority: ToastPriority.Feedback,
            Category: "nightlight"));
    }

    internal void NightLightWarmthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var dp = SettingsContainer?.DisplayPowerPanel;
        if (_isApplyingSettings || _settingsService == null || dp?.NightLightWarmthText == null) return;
        int kelvin = (int)Math.Round(e.NewValue);
        if (_settingsService.Current.NightLightWarmthKelvin == kelvin) return;

        dp.NightLightWarmthText.Text = $"{kelvin} K";
        _settingsService.Update(s => s.NightLightWarmthKelvin = kelvin, nameof(AppSettings.NightLightWarmthKelvin));

        if (_settingsService.Current.EnableNightLightSim)
        {
            DisplayTopologyService.SetNightLight(true, kelvin);
        }
    }

    internal void ChargingPulseToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        bool next = !_settingsService.Current.EnableChargingPulse;
        _settingsService.Update(s => s.EnableChargingPulse = next, nameof(AppSettings.EnableChargingPulse));
        UpdateDisplayAndBatterySettingsUI();
        _machine.PushToast(new ToastContent(
            "\uE945",
            "Charging Pulse",
            next ? "MagSafe charging liquid expansion active" : "Charging animation disabled",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "power"));
    }

    internal void LowBattThresholdBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        int cur = _settingsService.Current.LowBatteryAlertThreshold;
        int next = cur switch
        {
            20 => 15,
            15 => 10,
            10 => 0,
            _ => 20
        };
        _settingsService.Update(s => s.LowBatteryAlertThreshold = next, nameof(AppSettings.LowBatteryAlertThreshold));
        UpdateDisplayAndBatterySettingsUI();
        _machine.PushToast(new ToastContent(
            "\uEBA0",
            "Low Battery Warning Threshold",
            next > 0 ? $"Alert triggers at {next}%" : "Low battery alert disabled",
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "power"));
    }

    internal void AutoPowerSaverToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        bool next = !_settingsService.Current.AutoPowerPlanOnBattery;
        _settingsService.Update(s => s.AutoPowerPlanOnBattery = next, nameof(AppSettings.AutoPowerPlanOnBattery));
        UpdateDisplayAndBatterySettingsUI();
        _machine.PushToast(new ToastContent(
            "\uEC0A",
            "Battery Saver Automation",
            next ? "Auto-engages Power Saver scheme on battery" : "Battery saver auto-switch off",
            next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "power"));
    }

    internal void AutoPowerAcToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        bool next = !_settingsService.Current.AutoPowerPlanOnAc;
        _settingsService.Update(s => s.AutoPowerPlanOnAc = next, nameof(AppSettings.AutoPowerPlanOnAc));
        UpdateDisplayAndBatterySettingsUI();
        _machine.PushToast(new ToastContent(
            "\uE945",
            "AC Performance Automation",
            next ? "Auto-engages Performance/Balanced scheme on AC" : "AC performance auto-switch off",
            next ? ThemeBrushes.AmberColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "power"));
    }

    internal void HighLoadAlertsToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        bool next = !_settingsService.Current.EnableHighLoadAlerts;
        _settingsService.Update(s => s.EnableHighLoadAlerts = next, nameof(AppSettings.EnableHighLoadAlerts));
        if (!next)
        {
            _isHighLoadAlertActive = false;
            UpdateSecondaryDropletTarget();
        }
        UpdateDisplayAndBatterySettingsUI();
        _machine.PushToast(new ToastContent(
            "\uE9D9",
            "High-Load Droplet Alerts",
            next ? "Detached CPU & RAM overload droplet enabled" : "High-load overload droplet alerts muted",
            next ? ThemeBrushes.AmberColor : ThemeBrushes.MutedGreyColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "system"));
    }

    internal void HighCpuThresholdBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        int cur = _settingsService.Current.HighCpuAlertThreshold;
        int next = cur switch
        {
            75 => 80,
            80 => 85,
            85 => 90,
            90 => 95,
            _ => 75
        };
        _settingsService.Update(s => s.HighCpuAlertThreshold = next, nameof(AppSettings.HighCpuAlertThreshold));
        UpdateDisplayAndBatterySettingsUI();
        _machine.PushToast(new ToastContent(
            "\uE9D9",
            "CPU Alert Threshold",
            $"High CPU droplet triggers at {next}%+",
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "system"));
    }

    internal void HighRamThresholdBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        int cur = _settingsService.Current.HighRamAlertThreshold;
        int next = cur switch
        {
            75 => 80,
            80 => 85,
            85 => 90,
            90 => 95,
            _ => 75
        };
        _settingsService.Update(s => s.HighRamAlertThreshold = next, nameof(AppSettings.HighRamAlertThreshold));
        UpdateDisplayAndBatterySettingsUI();
        _machine.PushToast(new ToastContent(
            "\uE9D9",
            "RAM Alert Threshold",
            $"High RAM droplet triggers at {next}%+",
            ThemeBrushes.RedColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "system"));
    }

    private void UpdateDisplayAndBatterySettingsUI()
    {
        if (_settingsService == null) return;
        var s = _settingsService.Current;
        var dp = SettingsContainer?.DisplayPowerPanel;

        // Night Light (Settings panel switch + warmth dimming)
        if (dp != null)
        {
            if (dp.NightLightToggleSwitch != null)
            {
                dp.NightLightToggleSwitch.IsChecked = s.EnableNightLightSim;
            }
            if (dp.NightLightWarmthGroup != null)
            {
                dp.NightLightWarmthGroup.IsEnabled = s.EnableNightLightSim;
                dp.NightLightWarmthGroup.Opacity = s.EnableNightLightSim ? 1.0 : 0.45;
            }
            if (dp.NightLightWarmthText != null)
            {
                dp.NightLightWarmthText.Text = $"{s.NightLightWarmthKelvin} K";
            }
            if (dp.NightLightWarmthSlider != null && Math.Abs(dp.NightLightWarmthSlider.Value - s.NightLightWarmthKelvin) > 1)
            {
                dp.NightLightWarmthSlider.Value = s.NightLightWarmthKelvin;
            }

            if (dp.ChargingPulseToggleSwitch != null)
                dp.ChargingPulseToggleSwitch.IsChecked = s.EnableChargingPulse;

            if (dp.LowBattThresholdText != null)
            {
                dp.LowBattThresholdText.Text = s.LowBatteryAlertThreshold > 0 ? $"{s.LowBatteryAlertThreshold}%" : "Off";
                dp.LowBattThresholdText.Foreground = s.LowBatteryAlertThreshold > 0 ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;
            }

            if (dp.AutoPowerSaverToggleSwitch != null)
                dp.AutoPowerSaverToggleSwitch.IsChecked = s.AutoPowerPlanOnBattery;

            if (dp.AutoPowerAcToggleSwitch != null)
                dp.AutoPowerAcToggleSwitch.IsChecked = s.AutoPowerPlanOnAc;

            if (dp.HighLoadAlertsToggleSwitch != null)
                dp.HighLoadAlertsToggleSwitch.IsChecked = s.EnableHighLoadAlerts;

            if (dp.HighCpuThresholdText != null)
            {
                dp.HighCpuThresholdText.Text = $"{s.HighCpuAlertThreshold}%";
                dp.HighCpuThresholdText.Foreground = s.EnableHighLoadAlerts ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;
            }

            if (dp.HighRamThresholdText != null)
            {
                dp.HighRamThresholdText.Text = $"{s.HighRamAlertThreshold}%";
                dp.HighRamThresholdText.Foreground = s.EnableHighLoadAlerts ? ThemeBrushes.Red : ThemeBrushes.SecondaryText;
            }
        }

        if (_batteryContainer != null && NightLightQuickBtn != null && NightLightQuickText != null)
        {
            if (s.EnableNightLightSim)
            {
                NightLightQuickBtn.Background = ThemeBrushes.NightLightActiveBg;
                NightLightQuickBtn.BorderBrush = ThemeBrushes.AmberBorderActive;
                NightLightQuickText.Text = "Night Light On";
                NightLightQuickText.Foreground = ThemeBrushes.Amber;
                if (NightLightQuickIcon != null) NightLightQuickIcon.Foreground = ThemeBrushes.Amber;
            }
            else
            {
                NightLightQuickBtn.Background = ThemeBrushes.ButtonSubtleBg;
                NightLightQuickBtn.BorderBrush = ThemeBrushes.BorderSubtle;
                NightLightQuickText.Text = "Night Light";
                NightLightQuickText.Foreground = ThemeBrushes.SecondaryText;
                if (NightLightQuickIcon != null) NightLightQuickIcon.Foreground = ThemeBrushes.SecondaryText;
            }
        }

        UpdateMonitorUI();
        UpdateDisplayTopologyBadges();
    }

    internal void PlanPerfCard_Click(object sender, MouseButtonEventArgs e)
    {
        _powerPlanService.SetActiveScheme("Performance");
        _machine.PushToast(new ToastContent("\uE945", "Best Performance", "Windows Power Scheme Applied", ThemeBrushes.AmberColor, TimeSpan.FromSeconds(2.0), "battery", ToastPriority.Feedback, "powerplan"));
    }

    internal void PlanBalancedCard_Click(object sender, MouseButtonEventArgs e)
    {
        _powerPlanService.SetActiveScheme("Balanced");
        _machine.PushToast(new ToastContent("\uE74C", "Balanced Mode", "Recommended Scheme Applied", ThemeBrushes.BlueColor, TimeSpan.FromSeconds(2.0), "battery", ToastPriority.Feedback, "powerplan"));
    }

    internal void PlanSaverCard_Click(object sender, MouseButtonEventArgs e)
    {
        _powerPlanService.SetActiveScheme("PowerSaver");
        _machine.PushToast(new ToastContent("\uEC0A", "Power Saver", "Battery Saver Scheme Applied", ThemeBrushes.GreenColor, TimeSpan.FromSeconds(2.0), "battery", ToastPriority.Feedback, "powerplan"));
    }

}
