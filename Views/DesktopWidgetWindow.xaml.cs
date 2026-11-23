using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

public enum DesktopWidgetKind
{
    Vitals,
    Battery,
    Network,
    Weather,
    Timer,
    Media,
    Storage,
    Calendar,
    Audio
}

/// <summary>
/// Detachable, macOS-style desktop widget window pinned directly from <see cref="Widgets.WidgetsGalleryView"/>.
/// Maintains zero idle CPU when unpinned; only runs a low-frequency background refresh timer while open.
/// Features per-widget interactive controls, ambient halo tinting, translucent ghost mode, and magnetic inter-widget snapping.
/// </summary>
public partial class DesktopWidgetWindow : Window
{
    private static readonly Dictionary<DesktopWidgetKind, DesktopWidgetWindow> ActiveWidgets = new();
    public static event Action? ActiveWidgetsChanged;

    private static readonly Color AmberColor = Color.FromRgb(0xFF, 0x9F, 0x0A);
    private static readonly Color GreenColor = Color.FromRgb(0x30, 0xD1, 0x58);
    private static readonly Color BlueColor = Color.FromRgb(0x0A, 0x84, 0xFF);
    private static readonly Color RedAlertColor = Color.FromRgb(0xFF, 0x45, 0x3A);
    private static readonly Color GoldColor = Color.FromRgb(0xFF, 0xD6, 0x0A);
    private static readonly Color CyanColor = Color.FromRgb(0x32, 0xAD, 0xE6);
    private static readonly Color PurpleColor = Color.FromRgb(0xBF, 0x5A, 0xF2);
    private static readonly Color RoseColor = Color.FromRgb(0xFF, 0x37, 0x5F);

    private static readonly SolidColorBrush CpuBarBrush = ThemeBrushes.CreateFrozen(AmberColor);
    private static readonly SolidColorBrush GreenBarBrush = ThemeBrushes.CreateFrozen(GreenColor);
    private static readonly SolidColorBrush BlueBarBrush = ThemeBrushes.CreateFrozen(BlueColor);
    private static readonly SolidColorBrush RedAlertBarBrush = ThemeBrushes.CreateFrozen(RedAlertColor);
    private static readonly SolidColorBrush GoldBarBrush = ThemeBrushes.CreateFrozen(GoldColor);
    private static readonly SolidColorBrush CyanBarBrush = ThemeBrushes.CreateFrozen(CyanColor);
    private static readonly SolidColorBrush PurpleBarBrush = ThemeBrushes.CreateFrozen(PurpleColor);
    private static readonly SolidColorBrush RoseBarBrush = ThemeBrushes.CreateFrozen(RoseColor);

    private static readonly SolidColorBrush ActivePillBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x28, 0x30, 0xD1, 0x58));
    private static readonly SolidColorBrush InactivePillBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush InactivePillBorderBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush AwakeActiveBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x26, 0xFF, 0x9F, 0x0A));
    private static readonly SolidColorBrush AwakeActiveBorderBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x70, 0xFF, 0x9F, 0x0A));

    private readonly DesktopWidgetKind _kind;
    private readonly IslandWindow? _island;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isTopmostPinned;
    private bool _isGhostTranslucent;
    private string? _transientFooterMessage;
    private long _transientFooterUntilTick;
    private bool _isChargeShimmerRunning;
    private double _lastBatteryFillWidth = -1;

    public DesktopWidgetKind Kind => _kind;

    public static int ActiveCount => ActiveWidgets.Count;

    public static bool IsPinned(DesktopWidgetKind kind) => ActiveWidgets.ContainsKey(kind);

    public static DesktopWidgetWindow Toggle(DesktopWidgetKind kind, IslandWindow? island)
    {
        if (ActiveWidgets.TryGetValue(kind, out var existing))
        {
            existing.Close();
            return existing;
        }

        int slotIndex = ActiveWidgets.Count;
        var win = new DesktopWidgetWindow(kind, island);
        ActiveWidgets[kind] = win;

        var workArea = SystemParameters.WorkArea;
        double rightEdge = workArea.Right - win.Width - 16.0;
        double topStart = workArea.Top + 52.0;
        double colOffset = (slotIndex / 4) * (win.Width + 8.0);
        double rowOffset = (slotIndex % 4) * (win.Height + 6.0);

        win.Left = Math.Max(workArea.Left + 12.0, rightEdge - colOffset);
        win.Top = Math.Min(workArea.Bottom - win.Height - 12.0, topStart + rowOffset);

        win.Show();
        ActiveWidgetsChanged?.Invoke();
        return win;
    }

    public static void CloseAll()
    {
        foreach (var win in ActiveWidgets.Values.ToArray())
        {
            try { win.Close(); } catch { }
        }
        ActiveWidgets.Clear();
        ActiveWidgetsChanged?.Invoke();
    }

    public DesktopWidgetWindow(DesktopWidgetKind kind, IslandWindow? island = null)
    {
        _kind = kind;
        _island = island ?? IslandWindow.Instance;
        InitializeComponent();

        ConfigureAppearance();
        AttachMicroInteractions();

        TimeSpan interval = kind is DesktopWidgetKind.Timer or DesktopWidgetKind.Media
            ? TimeSpan.FromSeconds(1)
            : (kind is DesktopWidgetKind.Vitals or DesktopWidgetKind.Network or DesktopWidgetKind.Audio
                ? TimeSpan.FromSeconds(2)
                : TimeSpan.FromSeconds(5));

        _refreshTimer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => RefreshData(), Dispatcher);
        Loaded += (_, _) =>
        {
            RefreshData();
            _refreshTimer.Start();
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        StopChargeShimmer();
        if (ActiveWidgets.TryGetValue(_kind, out var current) && ReferenceEquals(current, this))
        {
            ActiveWidgets.Remove(_kind);
            ActiveWidgetsChanged?.Invoke();
        }
        base.OnClosed(e);
    }

    private void AttachMicroInteractions()
    {
        BentoMotion.AttachInteractiveSpring(InlineActionBtn, hoverScale: 1.08, pressScale: 0.92);
        BentoMotion.AttachInteractiveSpring(MediaPrevBtn, hoverScale: 1.08, pressScale: 0.92);
        BentoMotion.AttachInteractiveSpring(MediaNextBtn, hoverScale: 1.08, pressScale: 0.92);
        BentoMotion.AttachInteractiveSpring(FooterUtilityBtn, hoverScale: 1.04, pressScale: 0.95);
        BentoMotion.AttachInteractiveSpring(MediaArtworkHost, hoverScale: 1.05, pressScale: 0.95);
        BentoMotion.AttachInteractiveSpring(BatteryVisualCellHost, hoverScale: 1.04, pressScale: 0.96);
        BentoMotion.AttachInteractiveSpring(PlanEcoBtn, hoverScale: 1.03, pressScale: 0.95);
        BentoMotion.AttachInteractiveSpring(PlanBalBtn, hoverScale: 1.03, pressScale: 0.95);
        BentoMotion.AttachInteractiveSpring(PlanPerfBtn, hoverScale: 1.03, pressScale: 0.95);
    }

    private void ConfigureAppearance()
    {
        (string glyph, string title, Color accentColor, SolidColorBrush accentBrush) = _kind switch
        {
            DesktopWidgetKind.Vitals => ("\uE9D9", "SYSTEM VITALS", AmberColor, CpuBarBrush),
            DesktopWidgetKind.Battery => ("\uE945", "BATTERY & POWER", GreenColor, GreenBarBrush),
            DesktopWidgetKind.Network => ("\uE701", "NETWORK & WI-FI", BlueColor, BlueBarBrush),
            DesktopWidgetKind.Weather => ("\uE706", "LIVE WEATHER", GoldColor, GoldBarBrush),
            DesktopWidgetKind.Timer => ("\uE916", "FOCUS TIMER", AmberColor, CpuBarBrush),
            DesktopWidgetKind.Media => ("\uE8D6", "NOW PLAYING", RoseColor, RoseBarBrush),
            DesktopWidgetKind.Storage => ("\uEDA2", "SYSTEM STORAGE", CyanColor, CyanBarBrush),
            DesktopWidgetKind.Calendar => ("\uE787", "NEXT EVENT", GoldColor, GoldBarBrush),
            DesktopWidgetKind.Audio => ("\uE7F5", "AUDIO OUTPUT", GreenColor, GreenBarBrush),
            _ => ("\uE74C", "WIDGET", AmberColor, CpuBarBrush)
        };

        HeaderGlyphText.Text = glyph;
        HeaderGlyphText.Foreground = accentBrush;
        HeaderTitleText.Text = title;
        AmbientHaloStop.Color = accentColor;
        StatusBadgeText.Foreground = accentBrush;
        HeaderIconBadge.BorderBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x55, accentColor.R, accentColor.G, accentColor.B));
        HeaderIconBadge.Background = ThemeBrushes.CreateFrozen(Color.FromArgb(0x1E, accentColor.R, accentColor.G, accentColor.B));

        // Reset bespoke widget panels
        MediaArtworkHost.Visibility = _kind == DesktopWidgetKind.Media ? Visibility.Visible : Visibility.Collapsed;
        CalendarDateLeafHost.Visibility = _kind == DesktopWidgetKind.Calendar ? Visibility.Visible : Visibility.Collapsed;
        BatteryVisualCellHost.Visibility = _kind == DesktopWidgetKind.Battery ? Visibility.Visible : Visibility.Collapsed;
        NetworkDualCardsPanel.Visibility = _kind == DesktopWidgetKind.Network ? Visibility.Visible : Visibility.Collapsed;
        BatteryPlanStrip.Visibility = _kind == DesktopWidgetKind.Battery ? Visibility.Visible : Visibility.Collapsed;
        TimerPresetStrip.Visibility = _kind == DesktopWidgetKind.Timer ? Visibility.Visible : Visibility.Collapsed;
        AudioControlStrip.Visibility = _kind == DesktopWidgetKind.Audio ? Visibility.Visible : Visibility.Collapsed;

        if (_kind == DesktopWidgetKind.Battery)
        {
            Height = 172;
            HeroValueText.FontSize = 21.0;
        }

        bool isMedia = _kind == DesktopWidgetKind.Media;
        bool isTimer = _kind == DesktopWidgetKind.Timer;
        MediaPrevBtn.Visibility = isMedia ? Visibility.Visible : Visibility.Collapsed;
        MediaNextBtn.Visibility = isMedia ? Visibility.Visible : Visibility.Collapsed;
        InlineActionBtn.Visibility = (isMedia || isTimer) ? Visibility.Visible : Visibility.Collapsed;
        InlineActionGlyph.Foreground = accentBrush;

        if (isTimer)
        {
            InlineActionBtn.ToolTip = "Start / Pause Countdown";
        }
        else if (isMedia)
        {
            InlineActionBtn.ToolTip = "Play / Pause Media";
        }

        // Configure context-aware footer utility pill button
        switch (_kind)
        {
            case DesktopWidgetKind.Vitals:
                FooterUtilityBtn.Visibility = Visibility.Visible;
                FooterUtilityText.Text = "Boost RAM";
                FooterUtilityBtn.ToolTip = "Trim idle process working sets to free RAM";
                break;
            case DesktopWidgetKind.Battery:
                FooterUtilityBtn.Visibility = Visibility.Visible;
                FooterUtilityText.Text = "Awake";
                FooterUtilityBtn.ToolTip = "Toggle Keep Awake (Caffeine Mode)";
                break;
            case DesktopWidgetKind.Network:
                FooterUtilityBtn.Visibility = Visibility.Visible;
                FooterUtilityText.Text = "Copy IP";
                FooterUtilityBtn.ToolTip = "Copy Local IPv4 Address to Clipboard";
                break;
            case DesktopWidgetKind.Weather:
                FooterUtilityBtn.Visibility = Visibility.Visible;
                FooterUtilityText.Text = "Sync ↻";
                FooterUtilityBtn.ToolTip = "Refresh Live Open-Meteo Weather";
                break;
            case DesktopWidgetKind.Storage:
                FooterUtilityBtn.Visibility = Visibility.Visible;
                FooterUtilityText.Text = "Explorer ↗";
                FooterUtilityBtn.ToolTip = "Open System Drive in Windows Explorer";
                break;
            case DesktopWidgetKind.Calendar:
                FooterUtilityBtn.Visibility = Visibility.Visible;
                FooterUtilityText.Text = "Agenda ↗";
                FooterUtilityBtn.ToolTip = "Join meeting or open Calendar & Tasks";
                break;
            default:
                FooterUtilityBtn.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void SetTransientFooter(string message, int durationMs = 2600)
    {
        _transientFooterMessage = message;
        _transientFooterUntilTick = Environment.TickCount64 + durationMs;
        FooterText.Text = message;
    }

    private void ApplyFooterText(string defaultText)
    {
        if (_transientFooterMessage != null)
        {
            if (Environment.TickCount64 < _transientFooterUntilTick)
            {
                FooterText.Text = _transientFooterMessage;
                return;
            }
            _transientFooterMessage = null;
        }
        FooterText.Text = defaultText;
    }

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

    private void RootCard_MouseEnter(object sender, MouseEventArgs e)
    {
        HeaderControlsCluster.Opacity = 1.0;
    }

    private void RootCard_MouseLeave(object sender, MouseEventArgs e)
    {
        HeaderControlsCluster.Opacity = 0.68;
    }

    private void RootCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            OpenInIslandBtn_Click(sender, e);
            return;
        }
        try
        {
            DragMove();
            SnapToWorkAreaEdges();
        }
        catch (Exception ex)
        {
            App.LogDebug("DesktopWidgetWindow", ex);
        }
    }

    private void SnapToWorkAreaEdges()
    {
        var work = SystemParameters.WorkArea;
        const double snapMargin = 16.0;
        const double edgePad = 10.0;

        if (Math.Abs(Left - work.Left) <= snapMargin) Left = work.Left + edgePad;
        else if (Math.Abs((Left + Width) - work.Right) <= snapMargin) Left = work.Right - Width - edgePad;

        if (Math.Abs(Top - work.Top) <= snapMargin) Top = work.Top + edgePad;
        else if (Math.Abs((Top + Height) - work.Bottom) <= snapMargin) Top = work.Bottom - Height - edgePad;

        // Inter-widget magnetic alignment snapping (align columns and rows cleanly)
        const double peerSnap = 14.0;
        const double peerGap = 6.0;
        foreach (var peer in ActiveWidgets.Values)
        {
            if (ReferenceEquals(peer, this)) continue;

            // Snap X to align column with peer
            if (Math.Abs(Left - peer.Left) <= peerSnap)
                Left = peer.Left;
            else if (Math.Abs(Left - (peer.Left + peer.Width + peerGap)) <= peerSnap)
                Left = peer.Left + peer.Width + peerGap;
            else if (Math.Abs((Left + Width + peerGap) - peer.Left) <= peerSnap)
                Left = peer.Left - Width - peerGap;

            // Snap Y to stack directly below or above peer
            if (Math.Abs(Top - peer.Top) <= peerSnap)
                Top = peer.Top;
            else if (Math.Abs(Top - (peer.Top + peer.Height + peerGap)) <= peerSnap)
                Top = peer.Top + peer.Height + peerGap;
            else if (Math.Abs((Top + Height + peerGap) - peer.Top) <= peerSnap)
                Top = peer.Top - Height - peerGap;
        }
    }

    private void OpacityToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _isGhostTranslucent = !_isGhostTranslucent;
        RootCard.Opacity = _isGhostTranslucent ? 0.78 : 1.0;
        OpacityToggleGlyph.Foreground = _isGhostTranslucent ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;
        e.Handled = true;
    }

    private void PinTopmostBtn_Click(object sender, RoutedEventArgs e)
    {
        _isTopmostPinned = !_isTopmostPinned;
        Topmost = _isTopmostPinned;
        PinTopmostGlyph.Foreground = _isTopmostPinned ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;
        e.Handled = true;
    }

    private void OpenInIslandBtn_Click(object sender, RoutedEventArgs e)
    {
        var target = _kind switch
        {
            DesktopWidgetKind.Vitals => ExpandedView.SystemHud,
            DesktopWidgetKind.Battery => ExpandedView.Battery,
            DesktopWidgetKind.Network => ExpandedView.Network,
            DesktopWidgetKind.Weather => ExpandedView.Calendar,
            DesktopWidgetKind.Timer => ExpandedView.Media,
            DesktopWidgetKind.Media => ExpandedView.Media,
            DesktopWidgetKind.Storage => ExpandedView.SystemHud,
            DesktopWidgetKind.Calendar => ExpandedView.Calendar,
            DesktopWidgetKind.Audio => ExpandedView.Audio,
            _ => ExpandedView.WidgetsGallery
        };
        _island?.NavigateToView(target);
        e.Handled = true;
    }

    private void CloseWidgetBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    private void InlineActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_kind == DesktopWidgetKind.Timer)
        {
            _island?.CountdownServiceInstance?.TogglePause();
        }
        else if (_kind == DesktopWidgetKind.Media)
        {
            _island?.MediaServiceInstance?.TogglePlayPause();
        }
        RefreshData();
        e.Handled = true;
    }

    private void MediaPrevBtn_Click(object sender, RoutedEventArgs e)
    {
        _island?.MediaServiceInstance?.Previous();
        RefreshData();
        e.Handled = true;
    }

    private void MediaNextBtn_Click(object sender, RoutedEventArgs e)
    {
        _island?.MediaServiceInstance?.Next();
        RefreshData();
        e.Handled = true;
    }

    private void MediaArtworkHost_Click(object sender, MouseButtonEventArgs e)
    {
        if (_island?.MediaServiceInstance?.FocusActivePlayerWindow() != true)
        {
            _island?.NavigateToView(ExpandedView.Media);
        }
        e.Handled = true;
    }

    private void BatteryPlanBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string schemeKey })
        {
            if (_island?.PowerPlanServiceInstance is { } pwr)
            {
                pwr.SetActiveScheme(schemeKey);
            }
            UpdateBatteryPlanPillHighlight(schemeKey);
            SetTransientFooter($"Power plan: {schemeKey}");
            RefreshData();
        }
        e.Handled = true;
    }

    private void TimerPresetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string rawMin } && int.TryParse(rawMin, out int minutes) && minutes > 0)
        {
            _island?.CountdownServiceInstance?.StartCountdown(TimeSpan.FromMinutes(minutes), $"Focus {minutes}m");
            SetTransientFooter($"Started {minutes}m focus session");
            RefreshData();
        }
        e.Handled = true;
    }

    private void TimerResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _island?.CountdownServiceInstance?.Reset();
        SetTransientFooter("Timer reset to 25:00");
        RefreshData();
        e.Handled = true;
    }

    private void AudioMutePillBtn_Click(object sender, RoutedEventArgs e)
    {
        _island?.VolumeServiceInstance?.ToggleMute();
        RefreshData();
        e.Handled = true;
    }

    private void AudioStepVolBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string rawDelta } &&
            float.TryParse(rawDelta, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float delta))
        {
            _island?.VolumeServiceInstance?.StepVolume(delta);
            RefreshData();
        }
        e.Handled = true;
    }

    private void AudioSwitchOutputBtn_Click(object sender, RoutedEventArgs e)
    {
        var epSvc = _island?.AudioEndpointServiceInstance;
        if (epSvc != null && epSvc.Endpoints.Count > 1)
        {
            var list = epSvc.Endpoints;
            int currentIdx = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsDefault)
                {
                    currentIdx = i;
                    break;
                }
            }
            var nextEp = list[(currentIdx + 1) % list.Count];
            epSvc.SetDefaultEndpoint(nextEp.Id);
            SetTransientFooter($"Switched to {nextEp.Name}");
        }
        else
        {
            _island?.NavigateToView(ExpandedView.Audio);
        }
        RefreshData();
        e.Handled = true;
    }

    private void FooterUtilityBtn_Click(object sender, RoutedEventArgs e)
    {
        switch (_kind)
        {
            case DesktopWidgetKind.Vitals:
            {
                if (_island?.SystemPerfServiceInstance is { } sys)
                {
                    var res = sys.PurgeIdleRam();
                    if (res.OnCooldown)
                    {
                        SetTransientFooter($"Cooldown ({res.CooldownRemainingSeconds}s remaining)");
                    }
                    else
                    {
                        SetTransientFooter($"Freed {Math.Max(12, (int)res.FreedMb)} MB ({res.TrimmedProcesses} apps)");
                    }
                }
                else
                {
                    IslandMemoryManager.ExecuteConservativeTrim();
                    SetTransientFooter("Working set trimmed");
                }
                RefreshData();
                break;
            }
            case DesktopWidgetKind.Battery:
            {
                _island?.CaffeineQuickBtn_Click(sender, e);
                SetTransientFooter(_island?.IsCaffeineActive == true
                    ? $"Keep Awake: {_island.CaffeineShortLabel}"
                    : "Keep Awake: Off");
                RefreshData();
                break;
            }
            case DesktopWidgetKind.Network:
            {
                string ip = _island?.NetworkServiceInstance?.Current?.LocalIpv4 ?? "";
                if (!string.IsNullOrWhiteSpace(ip) && ip != "—")
                {
                    try
                    {
                        Clipboard.SetText(ip);
                        SetTransientFooter($"Copied {ip} to clipboard");
                    }
                    catch { }
                }
                break;
            }
            case DesktopWidgetKind.Weather:
            {
                if (_island?.WeatherServiceInstance is { } weather)
                {
                    SetTransientFooter("Syncing Open-Meteo...");
                    _ = weather.RefreshAsync();
                }
                break;
            }
            case DesktopWidgetKind.Storage:
            {
                try
                {
                    string root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
                    using var _ = Process.Start(new ProcessStartInfo("explorer.exe", root) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    App.LogDebug("DesktopWidgetWindow", ex);
                }
                break;
            }
            case DesktopWidgetKind.Calendar:
            {
                var next = _island?.CalendarServiceInstance?.NextEvent;
                if (next != null && !string.IsNullOrWhiteSpace(next.JoinUrl))
                {
                    SafeProcessLauncher.TryOpenUrl(next.JoinUrl);
                }
                else
                {
                    _island?.NavigateToView(ExpandedView.Calendar);
                }
                break;
            }
        }
        e.Handled = true;
    }
}
