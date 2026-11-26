using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

public partial class DesktopWidgetWindow
{
    public void RefreshData()
    {
        const double maxBarWidth = 224.0;

        switch (_kind)
        {
            case DesktopWidgetKind.Vitals:
            {
                var snap = _island?.SystemPerfServiceInstance?.LatestSnapshot;
                if (snap.HasValue)
                {
                    var s = snap.Value;
                    HeroValueText.Text = $"CPU {s.CpuPercent}%";
                    string hogPart = s.HogCount > 0 && !string.IsNullOrWhiteSpace(s.Hog1.ExeName)
                        ? $" · {s.Hog1.ExeName}"
                        : "";
                    HeroSubtitleText.Text = $"RAM {s.RamUsedGb:0.0}/{s.RamTotalGb:0} GB ({s.RamPercent}%){hogPart}";

                    MetricBarHost.Visibility = Visibility.Visible;
                    MetricBarFill.Background = s.IsHighCpu ? RedAlertBarBrush : CpuBarBrush;
                    MetricBarFill.Width = Math.Clamp(s.CpuPercent / 100.0 * maxBarWidth, 6.0, maxBarWidth);

                    MetricBar2Host.Visibility = Visibility.Visible;
                    MetricBar2Fill.Background = BlueBarBrush;
                    MetricBar2Fill.Width = Math.Clamp(s.RamPercent / 100.0 * maxBarWidth, 6.0, maxBarWidth);

                    if (s.Gpu.IsAvailable)
                    {
                        MetricBar3Host.Visibility = Visibility.Visible;
                        MetricBar3Fill.Background = PurpleBarBrush;
                        MetricBar3Fill.Width = Math.Clamp(s.Gpu.UtilizationPercent / 100.0 * maxBarWidth, 6.0, maxBarWidth);
                    }
                    else
                    {
                        MetricBar3Host.Visibility = Visibility.Collapsed;
                    }

                    ApplyFooterText(s.Gpu.IsAvailable
                        ? $"GPU {s.Gpu.UtilizationPercent}% · {s.Gpu.TemperatureC}°C"
                        : "CPU (amber) · RAM (blue)");
                    StatusBadge.Visibility = Visibility.Visible;
                    StatusBadgeText.Text = s.IsHighCpu || s.IsHighRam ? "LOAD" : "OK";
                    StatusBadgeText.Foreground = s.IsHighCpu || s.IsHighRam ? RedAlertBarBrush : CpuBarBrush;
                }
                else
                {
                    HeroValueText.Text = "CPU —%";
                    HeroSubtitleText.Text = "Awaiting telemetry snapshot";
                    ApplyFooterText("Double-click for System HUD");
                }
                break;
            }
            case DesktopWidgetKind.Battery:
            {
                MetricBarHost.Visibility = Visibility.Collapsed;
                MetricBar2Host.Visibility = Visibility.Collapsed;
                MetricBar3Host.Visibility = Visibility.Collapsed;

                var st = _island?.PowerPlanServiceInstance?.Current;
                UpdateBatteryVisualCell(st);

                if (st != null)
                {
                    HeroValueText.Text = st.HasBattery ? $"{st.BatteryPercent}%" : "AC";

                    string rateText = st.IsCharging ? $"Charging · {st.WattageText}" : st.StatusSubtitle;
                    string healthWh = BuildBatteryHealthAndWhText(st);
                    HeroSubtitleText.Text = string.IsNullOrEmpty(healthWh) ? rateText : $"{rateText}\n{healthWh}";
                    HeroSubtitleText.TextWrapping = TextWrapping.NoWrap;

                    UpdateBatteryPlanPillHighlight(st.ActiveSchemeKey);
                    ApplyFooterText(st.TimeEstimateText);
                    StatusBadge.Visibility = Visibility.Visible;
                    StatusBadgeText.Text = st.ActiveSchemeName;
                }
                else
                {
                    HeroValueText.Text = "100%";
                    HeroSubtitleText.Text = "Connected to AC";
                    UpdateBatteryPlanPillHighlight("Balanced");
                    ApplyFooterText("Continuous AC Power");
                    StatusBadge.Visibility = Visibility.Visible;
                    StatusBadgeText.Text = "Balanced";
                }

                SyncBatteryAwakeButton();
                break;
            }
            case DesktopWidgetKind.Network:
            {
                var net = _island?.NetworkServiceInstance?.Current;
                if (net != null)
                {
                    HeroValueText.FontSize = 15.0;
                    HeroValueText.Text = $"↓ {net.DownloadSpeedText}";
                    string connName = string.IsNullOrWhiteSpace(net.ConnectionName) ? "Network" : net.ConnectionName;
                    HeroSubtitleText.Text = $"↑ {net.UploadSpeedText} · {connName}";

                    NetDownPillText.Text = net.DownloadSpeedText;
                    NetUpPillText.Text = net.UploadSpeedText;

                    MetricBarHost.Visibility = Visibility.Collapsed;
                    MetricBar2Host.Visibility = Visibility.Collapsed;
                    MetricBar3Host.Visibility = Visibility.Collapsed;

                    ApplyFooterText($"IP {net.LocalIpv4}");
                    StatusBadge.Visibility = Visibility.Visible;
                    StatusBadgeText.Text = net.PingMs > 0 ? $"{net.PingMs} ms" : (net.IsConnected ? "ONLINE" : "OFF");
                }
                else
                {
                    HeroValueText.Text = "↓ 0 KB/s";
                    HeroSubtitleText.Text = "↑ 0 KB/s · Network";
                }
                break;
            }
            case DesktopWidgetKind.Weather:
            {
                var w = _island?.WeatherServiceInstance?.Current;
                if (w != null)
                {
                    HeaderGlyphText.Text = w.Glyph;
                    HeroValueText.Text = w.BadgeText;
                    HeroSubtitleText.Text = $"{w.Condition} · {w.LocationName}";
                    MetricBarHost.Visibility = Visibility.Collapsed;
                    MetricBar2Host.Visibility = Visibility.Collapsed;
                    MetricBar3Host.Visibility = Visibility.Collapsed;
                    ApplyFooterText(w.SummaryLine);
                    StatusBadge.Visibility = Visibility.Visible;
                    StatusBadgeText.Text = w.IsAvailable ? "LIVE" : "LOCAL";
                }
                else
                {
                    HeroValueText.Text = "—°C";
                    HeroSubtitleText.Text = "Open-Meteo weather offline";
                    ApplyFooterText("Click Sync to fetch forecast");
                }
                break;
            }
            case DesktopWidgetKind.Timer:
            {
                var timer = _island?.CountdownServiceInstance;
                if (timer != null)
                {
                    HeroValueText.Text = timer.FormatRemaining();
                    HeroSubtitleText.Text = timer.IsRunning
                        ? $"{timer.Label} · Running"
                        : (timer.IsActive ? $"{timer.Label} · Paused" : "Pomodoro Ready (25m)");
                    InlineActionGlyph.Text = timer.IsRunning ? "\uE769" : "\uE768";

                    double ratio = timer.TargetDuration.TotalSeconds > 0
                        ? Math.Clamp(1.0 - (timer.Remaining.TotalSeconds / timer.TargetDuration.TotalSeconds), 0.0, 1.0)
                        : 0.0;
                    MetricBarHost.Visibility = Visibility.Visible;
                    MetricBarFill.Background = CpuBarBrush;
                    MetricBarFill.Width = Math.Max(4.0, ratio * maxBarWidth);
                    MetricBar2Host.Visibility = Visibility.Collapsed;
                    MetricBar3Host.Visibility = Visibility.Collapsed;

                    ApplyFooterText(timer.IsRunning ? "Focus sprint in progress" : "Select preset or press play");
                    StatusBadge.Visibility = Visibility.Visible;
                    StatusBadgeText.Text = timer.IsRunning ? "ACTIVE" : (timer.IsActive ? "PAUSED" : "READY");
                }
                else
                {
                    HeroValueText.Text = "25:00";
                    HeroSubtitleText.Text = "Pomodoro Ready";
                }
                break;
            }
            case DesktopWidgetKind.Media:
            {
                var media = _island?.MediaServiceInstance?.Current;
                bool hasMedia = media != null && !string.IsNullOrWhiteSpace(media.Title);
                HeroValueText.FontSize = 13.0;
                HeroValueText.Text = hasMedia ? media!.Title : "No Media Playing";
                HeroSubtitleText.Text = hasMedia
                    ? (string.IsNullOrWhiteSpace(media!.Artist) ? "Unknown Artist" : media.Artist)
                    : "Play audio to sync";
                InlineActionGlyph.Text = (media?.IsPlaying ?? false) ? "\uE769" : "\uE768";

                if (media?.Artwork != null)
                {
                    MediaArtworkImage.Source = media.Artwork;
                    MediaArtworkImage.Visibility = Visibility.Visible;
                    MediaArtworkFallback.Visibility = Visibility.Collapsed;
                }
                else
                {
                    MediaArtworkImage.Visibility = Visibility.Collapsed;
                    MediaArtworkFallback.Visibility = Visibility.Visible;
                }

                if (media?.Accent is { } artAccent)
                {
                    AmbientHaloStop.Color = artAccent;
                }
                else
                {
                    AmbientHaloStop.Color = RoseColor;
                }

                MetricBarHost.Visibility = Visibility.Visible;
                MetricBarFill.Background = GreenBarBrush;
                double pct = (media != null && media.Duration.TotalSeconds > 0)
                    ? Math.Clamp(media.Position.TotalSeconds / media.Duration.TotalSeconds, 0.0, 1.0)
                    : 0.0;
                MetricBarFill.Width = Math.Max(4.0, pct * maxBarWidth);
                MetricBar2Host.Visibility = Visibility.Collapsed;
                MetricBar3Host.Visibility = Visibility.Collapsed;

                string brand = media?.BrandInfo?.DisplayName ?? "System Media";
                if (hasMedia && media!.Duration.TotalSeconds > 1)
                {
                    string posStr = $"{(int)media.Position.TotalMinutes}:{media.Position.Seconds:D2}";
                    string durStr = $"{(int)media.Duration.TotalMinutes}:{media.Duration.Seconds:D2}";
                    ApplyFooterText($"{brand} · {posStr} / {durStr}");
                }
                else
                {
                    ApplyFooterText(brand);
                }

                StatusBadge.Visibility = Visibility.Visible;
                StatusBadgeText.Text = (media?.IsPlaying ?? false) ? "PLAYING" : "PAUSED";
                break;
            }
            case DesktopWidgetKind.Storage:
            {
                var info = ReadSystemDriveSnapshot();
                HeroValueText.Text = $"{info.FreeGb:0.0} GB Free";
                HeroSubtitleText.Text = $"{info.UsedGb:0.0} / {info.TotalGb:0.0} GB used on {info.DriveLabel}";
                MetricBarHost.Visibility = Visibility.Visible;
                MetricBarFill.Background = info.UsedPercent >= 88 ? RedAlertBarBrush : CyanBarBrush;
                MetricBarFill.Width = Math.Clamp(info.UsedPercent / 100.0 * maxBarWidth, 6.0, maxBarWidth);
                MetricBar2Host.Visibility = Visibility.Collapsed;
                MetricBar3Host.Visibility = Visibility.Collapsed;
                ApplyFooterText($"{info.DriveFormat} · System Drive");
                StatusBadge.Visibility = Visibility.Visible;
                StatusBadgeText.Text = $"{info.UsedPercent:0}% USED";
                break;
            }
            case DesktopWidgetKind.Calendar:
            {
                var now = DateTime.Now;
                CalendarLeafDayOfWeekText.Text = now.ToString("ddd").ToUpperInvariant();
                CalendarLeafDayNumText.Text = now.Day.ToString();

                var next = _island?.CalendarServiceInstance?.NextEvent;
                HeroValueText.FontSize = 13.0;
                HeroValueText.Text = next != null ? next.Subject : "All Caught Up";
                HeroSubtitleText.Text = next != null ? next.TimeBadgeText : "No upcoming meetings today";
                MetricBarHost.Visibility = Visibility.Collapsed;
                MetricBar2Host.Visibility = Visibility.Collapsed;
                MetricBar3Host.Visibility = Visibility.Collapsed;

                FooterUtilityText.Text = (next != null && !string.IsNullOrWhiteSpace(next.JoinUrl)) ? "Join ↗" : "Agenda ↗";
                ApplyFooterText(now.ToString("ddd, MMM d · HH:mm"));
                StatusBadge.Visibility = Visibility.Visible;
                StatusBadgeText.Text = next != null ? "SOON" : "CLEAR";
                break;
            }
            case DesktopWidgetKind.Audio:
            {
                var volSvc = _island?.VolumeServiceInstance;
                var epSvc = _island?.AudioEndpointServiceInstance;
                float vol = volSvc?.CurrentVolume ?? 0.5f;
                bool muted = volSvc?.IsMuted ?? false;
                int pct = (int)Math.Round(Math.Clamp(vol, 0f, 1f) * 100f);

                HeroValueText.Text = muted ? "Muted" : $"{pct}% Volume";
                var currentEp = epSvc?.CurrentDefault;
                HeroSubtitleText.Text = currentEp != null ? currentEp.Name : "Default Audio Output";

                MetricBarHost.Visibility = Visibility.Visible;
                MetricBarFill.Background = muted ? RedAlertBarBrush : GreenBarBrush;
                MetricBarFill.Width = muted ? 6.0 : Math.Clamp(pct / 100.0 * maxBarWidth, 6.0, maxBarWidth);
                MetricBar2Host.Visibility = Visibility.Collapsed;
                MetricBar3Host.Visibility = Visibility.Collapsed;

                AudioMutePillBtn.Content = muted ? "Unmute" : "Mute";
                int count = epSvc?.Endpoints.Count ?? 1;
                ApplyFooterText(count > 1 ? $"{count} audio outputs ready" : "System master audio");
                StatusBadge.Visibility = Visibility.Visible;
                StatusBadgeText.Text = muted ? "MUTED" : $"{pct}%";
                break;
            }
        }
    }

    private static string BuildBatteryHealthAndWhText(PowerStatusInfo st)
    {
        if (!st.HasBattery) return string.Empty;
        string? whText = (st.FullChargeCapacityMWh.HasValue && st.DesignCapacityMWh.HasValue && st.DesignCapacityMWh.Value > 0)
            ? $"{st.FullChargeCapacityMWh.Value / 1000.0:0.0}/{st.DesignCapacityMWh.Value / 1000.0:0.0} Wh"
            : null;
        string? healthText = st.HealthPercent.HasValue ? $"Health {st.HealthPercent.Value}%" : null;
        if (healthText != null && whText != null) return $"{healthText} · {whText}";
        return healthText ?? whText ?? string.Empty;
    }

    private void SyncBatteryAwakeButton()
    {
        bool isAwake = _island?.IsCaffeineActive == true;
        FooterUtilityText.Text = isAwake ? (_island?.CaffeineShortLabel ?? "Awake") : "Awake";
        FooterUtilityText.Foreground = isAwake ? CpuBarBrush : ThemeBrushes.PrimaryText;
        if (isAwake)
        {
            FooterUtilityBtn.Background = AwakeActiveBgBrush;
            FooterUtilityBtn.BorderBrush = AwakeActiveBorderBrush;
        }
        else
        {
            FooterUtilityBtn.ClearValue(BackgroundProperty);
            FooterUtilityBtn.ClearValue(BorderBrushProperty);
        }
    }

    private void UpdateBatteryVisualCell(PowerStatusInfo? st)
    {
        const double maxFillWidth = 51.2;
        bool hasBattery = st == null || st.HasBattery;
        int pct = st == null || !st.HasBattery ? 100 : Math.Clamp(st.BatteryPercent, 0, 100);
        bool isCharging = st?.IsCharging == true;

        double targetWidth = Math.Clamp((pct / 100.0) * maxFillWidth, pct > 0 ? 6.0 : 0.0, maxFillWidth);
        if (_lastBatteryFillWidth >= 0 && Math.Abs(_lastBatteryFillWidth - targetWidth) > 0.5)
        {
            var anim = new DoubleAnimation(_lastBatteryFillWidth, targetWidth, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BatteryCellFillBar.BeginAnimation(WidthProperty, anim);
        }
        else
        {
            BatteryCellFillBar.BeginAnimation(WidthProperty, null);
            BatteryCellFillBar.Width = targetWidth;
        }
        _lastBatteryFillWidth = targetWidth;

        // Color threshold calibration using pre-frozen brushes
        if (isCharging || !hasBattery)
        {
            BatteryCellFillBar.Background = GreenBarBrush;
        }
        else if (pct <= 10)
        {
            BatteryCellFillBar.Background = RedAlertBarBrush;
        }
        else if (pct <= 20)
        {
            BatteryCellFillBar.Background = CpuBarBrush;
        }
        else
        {
            BatteryCellFillBar.Background = GreenBarBrush;
        }

        // Bolt or AC Plug overlay
        if (st != null && !st.HasBattery)
        {
            BatteryCellBoltGlyph.Text = "\uE83E";
            BatteryCellBoltGlyph.Visibility = Visibility.Visible;
        }
        else if (isCharging)
        {
            BatteryCellBoltGlyph.Text = "\uE945";
            BatteryCellBoltGlyph.Visibility = Visibility.Visible;
        }
        else
        {
            BatteryCellBoltGlyph.Visibility = Visibility.Collapsed;
        }

        // Zero-idle-CPU shimmer: animate strictly while charging below 100%
        if (isCharging && pct < 100)
        {
            StartChargeShimmer();
        }
        else
        {
            StopChargeShimmer();
        }
    }

    private void StartChargeShimmer()
    {
        if (_isChargeShimmerRunning) return;
        _isChargeShimmerRunning = true;
        BatteryChargeShimmer.Opacity = 0.85;
        var sweep = new DoubleAnimation(-26.0, 56.0, TimeSpan.FromSeconds(2.2))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        BatteryShimmerTranslate.BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    private void StopChargeShimmer()
    {
        if (!_isChargeShimmerRunning) return;
        _isChargeShimmerRunning = false;
        BatteryShimmerTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        BatteryShimmerTranslate.X = -26.0;
        BatteryChargeShimmer.Opacity = 0.0;
    }

    private void UpdateBatteryPlanPillHighlight(string? activeSchemeKey)
    {
        string key = string.IsNullOrWhiteSpace(activeSchemeKey) ? "Balanced" : activeSchemeKey;
        StylePlanButton(PlanEcoBtn, string.Equals(key, "PowerSaver", StringComparison.OrdinalIgnoreCase));
        StylePlanButton(PlanBalBtn, string.Equals(key, "Balanced", StringComparison.OrdinalIgnoreCase));
        StylePlanButton(PlanPerfBtn, string.Equals(key, "Performance", StringComparison.OrdinalIgnoreCase));
    }

    private static void StylePlanButton(Button btn, bool isActive)
    {
        if (btn == null) return;
        btn.Background = isActive ? ActivePillBgBrush : Brushes.Transparent;
        btn.BorderBrush = Brushes.Transparent;
        btn.Foreground = isActive ? GreenBarBrush : ThemeBrushes.PrimaryText;
    }

    internal static (string DriveLabel, string DriveFormat, double UsedGb, double FreeGb, double TotalGb, double UsedPercent) ReadSystemDriveSnapshot()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                const double gb = 1024.0 * 1024.0 * 1024.0;
                double total = drive.TotalSize / gb;
                double free = drive.AvailableFreeSpace / gb;
                double used = Math.Max(0.0, total - free);
                double pct = Math.Clamp((used / total) * 100.0, 0.0, 100.0);
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name.TrimEnd('\\') : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
                return (label, drive.DriveFormat, used, free, total, pct);
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("DesktopStorageWidget", ex);
        }
        return ("System (C:)", "NTFS", 0, 0, 0, 0);
    }
}
