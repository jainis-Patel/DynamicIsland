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
                        SafeClipboard.TrySetText(ip);
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
