using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class WidgetsGalleryView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? (Window.GetWindow(this) as IslandWindow) ?? (Application.Current?.MainWindow as IslandWindow);

    private bool _isExpanded;
    private DispatcherTimer? _expandedTimer;
    private string _activeFilter = "All";

    private static readonly SolidColorBrush PinnedBtnBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x32, 0xFF, 0x9F, 0x0A));

    public WidgetsGalleryView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateCardWidths();
        ApplyFilter(_activeFilter);
        RefreshAllWidgets();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SetExpanded(false);
    }

    public void SetExpanded(bool isExpanded)
    {
        if (_isExpanded == isExpanded) return;
        _isExpanded = isExpanded;

        var island = Island;
        if (island == null) return;

        if (_isExpanded)
        {
            // Subscribe to live telemetry strictly while expanded (Zero Idle CPU invariant)
            if (island.SystemPerfServiceInstance is { } sys)
            {
                sys.VitalsUpdated -= OnVitalsUpdated;
                sys.VitalsUpdated += OnVitalsUpdated;
                sys.SetHudExpanded(true);
            }

            if (island.NetworkServiceInstance is { } net)
            {
                net.Updated -= OnNetworkUpdated;
                net.Updated += OnNetworkUpdated;
                net.SetExpanded(true);
            }

            if (island.PowerPlanServiceInstance is { } pwr)
            {
                pwr.StatusChanged -= OnPowerStatusChanged;
                pwr.StatusChanged += OnPowerStatusChanged;
            }

            if (island.MediaServiceInstance is { } media)
            {
                media.Changed -= OnMediaChanged;
                media.Changed += OnMediaChanged;
                media.TrackChanged -= OnTrackChanged;
                media.TrackChanged += OnTrackChanged;
            }

            if (island.WeatherServiceInstance is { } weather)
            {
                weather.Updated -= OnWeatherUpdated;
                weather.Updated += OnWeatherUpdated;
            }

            if (island.CountdownServiceInstance is { } timer)
            {
                timer.StateChanged -= OnTimerStateChanged;
                timer.StateChanged += OnTimerStateChanged;
            }

            if (island.VolumeServiceInstance is { } vol)
            {
                vol.VolumeChanged -= OnVolumeChanged;
                vol.VolumeChanged += OnVolumeChanged;
            }

            if (island.AudioEndpointServiceInstance is { } audioEp)
            {
                audioEp.EndpointsChanged -= OnEndpointsChanged;
                audioEp.EndpointsChanged += OnEndpointsChanged;
            }

            island.PetService.MoodChanged -= OnPetMoodChanged;
            island.PetService.MoodChanged += OnPetMoodChanged;
            island.PetService.AffectionChanged -= OnPetAffectionChanged;
            island.PetService.AffectionChanged += OnPetAffectionChanged;

            DesktopWidgetWindow.ActiveWidgetsChanged -= OnDesktopWidgetsChanged;
            DesktopWidgetWindow.ActiveWidgetsChanged += OnDesktopWidgetsChanged;

            _expandedTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
            {
                if (_isExpanded) UpdateTimerUI();
            }, Dispatcher);
            _expandedTimer.Start();

            RefreshAllWidgets();
        }
        else
        {
            _expandedTimer?.Stop();

            // Unhook all event handlers immediately on collapse to ensure 0% CPU consumption
            if (island.SystemPerfServiceInstance is { } sys)
            {
                sys.VitalsUpdated -= OnVitalsUpdated;
                sys.SetHudExpanded(false);
            }

            if (island.NetworkServiceInstance is { } net)
            {
                net.Updated -= OnNetworkUpdated;
                net.SetExpanded(false);
            }

            if (island.PowerPlanServiceInstance is { } pwr)
            {
                pwr.StatusChanged -= OnPowerStatusChanged;
            }

            if (island.MediaServiceInstance is { } media)
            {
                media.Changed -= OnMediaChanged;
                media.TrackChanged -= OnTrackChanged;
            }

            if (island.WeatherServiceInstance is { } weather)
            {
                weather.Updated -= OnWeatherUpdated;
            }

            if (island.CountdownServiceInstance is { } timer)
            {
                timer.StateChanged -= OnTimerStateChanged;
            }

            if (island.VolumeServiceInstance is { } vol)
            {
                vol.VolumeChanged -= OnVolumeChanged;
            }

            if (island.AudioEndpointServiceInstance is { } audioEp)
            {
                audioEp.EndpointsChanged -= OnEndpointsChanged;
            }

            island.PetService.MoodChanged -= OnPetMoodChanged;
            island.PetService.AffectionChanged -= OnPetAffectionChanged;
            DesktopWidgetWindow.ActiveWidgetsChanged -= OnDesktopWidgetsChanged;
        }
    }

    public void RefreshAllWidgets()
    {
        var island = Island;
        if (island == null)
        {
            UpdateStorageUI();
            UpdateDesktopPinHeaderUI();
            return;
        }

        // 1. System Vitals
        if (island.SystemPerfServiceInstance is { } sys)
        {
            if (sys.LatestSnapshot is { } snap)
            {
                UpdateVitalsUI(snap);
            }
            if (!string.IsNullOrWhiteSpace(sys.HardwareSummary) && HardwareSummaryText != null)
            {
                HardwareSummaryText.Text = sys.HardwareSummary;
            }
        }

        // 2. Battery & Power
        if (island.PowerPlanServiceInstance is { } pwr)
        {
            UpdateBatteryUI(pwr.Current);
        }

        // 3. Network & Wi-Fi
        if (island.NetworkServiceInstance is { } net)
        {
            UpdateNetworkUI(net.Current);
        }

        // 4. Media & Now Playing
        if (island.MediaServiceInstance is { } media)
        {
            UpdateMediaUI(media.Current);
        }

        // 5. Calendar & Tasks
        UpdateCalendarUI();

        // 6. Notes Scratchpad
        UpdateNotesUI();

        // 7. Drop Shelf
        UpdateShelfUI();

        // 8. Weather & Forecast
        UpdateWeatherUI(island.WeatherServiceInstance?.Current);

        // 9. Focus Timer & Pomodoro
        UpdateTimerUI();

        // 10. Disk & Storage
        UpdateStorageUI();

        // 11. Audio Output & Volume
        UpdateAudioUI();

        // 12. Virtual Pet Companion
        UpdatePetUI();

        // 13. Mech Sounds State
        UpdateMechSoundsUI();

        // 14. Desktop Widget Pin Badge
        UpdateDesktopPinHeaderUI();
    }

    #region Layout & Filtering
    private void WidgetsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCardWidths();
    }

    private void UpdateCardWidths()
    {
        if (WidgetsScrollViewer == null) return;
        double avail = WidgetsScrollViewer.ViewportWidth > 40 ? WidgetsScrollViewer.ViewportWidth : 484.0;
        double cardWidth = Math.Max(200.0, Math.Floor((avail - 13.0) / 2.0));

        ApplyPanelCardWidth(LiveCardsWrapPanel, cardWidth);
        ApplyPanelCardWidth(ToolCardsWrapPanel, cardWidth);
    }

    private static void ApplyPanelCardWidth(WrapPanel? panel, double width)
    {
        if (panel == null) return;
        foreach (UIElement child in panel.Children)
        {
            if (child is FrameworkElement fe)
            {
                fe.Width = width;
            }
        }
    }

    private void FilterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string filter })
        {
            ApplyFilter(filter);
            WidgetsScrollViewer?.ScrollToVerticalOffset(0);
        }
    }

    private void BrowseLiveWidgets_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilter("Live");
        WidgetsScrollViewer?.ScrollToVerticalOffset(0);
    }

    internal void ApplyFilter(string filter)
    {
        _activeFilter = string.IsNullOrWhiteSpace(filter) ? "All" : filter;

        UpdateFilterPillVisual(FilterAllBtn, _activeFilter == "All");
        UpdateFilterPillVisual(FilterLiveBtn, _activeFilter == "Live");
        UpdateFilterPillVisual(FilterToolsBtn, _activeFilter == "Tools");
        UpdateFilterPillVisual(FilterPinnedBtn, _activeFilter == "Pinned");

        bool showAll = _activeFilter == "All";
        bool showLive = showAll || _activeFilter == "Live";
        bool showTools = showAll || _activeFilter == "Tools";
        bool showPinnedOnly = _activeFilter == "Pinned";

        int visibleLiveCount = 0;

        if (MediaHeroCard != null)
        {
            bool mediaVisible = showLive || (showPinnedOnly && DesktopWidgetWindow.IsPinned(DesktopWidgetKind.Media));
            MediaHeroCard.Visibility = mediaVisible ? Visibility.Visible : Visibility.Collapsed;
            if (mediaVisible) visibleLiveCount++;
        }

        var liveCards = new (FrameworkElement? Card, DesktopWidgetKind? Kind)[]
        {
            (CardVitals, DesktopWidgetKind.Vitals),
            (CardBattery, DesktopWidgetKind.Battery),
            (CardNetwork, DesktopWidgetKind.Network),
            (CardCalendar, DesktopWidgetKind.Calendar),
            (CardWeather, DesktopWidgetKind.Weather),
            (CardTimer, DesktopWidgetKind.Timer),
            (CardStorage, DesktopWidgetKind.Storage),
            (CardAudio, DesktopWidgetKind.Audio),
            (CardPet, null)
        };

        foreach (var (card, kind) in liveCards)
        {
            if (card == null) continue;
            bool visible = showLive || (showPinnedOnly && kind.HasValue && DesktopWidgetWindow.IsPinned(kind.Value));
            card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) visibleLiveCount++;
        }

        if (LiveSectionHeader != null)
            LiveSectionHeader.Visibility = visibleLiveCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (LiveCardsWrapPanel != null)
        {
            bool anyLiveWrapVisible = false;
            foreach (UIElement child in LiveCardsWrapPanel.Children)
            {
                if (child.Visibility == Visibility.Visible) { anyLiveWrapVisible = true; break; }
            }
            LiveCardsWrapPanel.Visibility = anyLiveWrapVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ToolsSectionHeader != null)
            ToolsSectionHeader.Visibility = showTools ? Visibility.Visible : Visibility.Collapsed;

        if (ToolCardsWrapPanel != null)
            ToolCardsWrapPanel.Visibility = showTools ? Visibility.Visible : Visibility.Collapsed;

        if (PinnedEmptyStateCard != null)
            PinnedEmptyStateCard.Visibility = (showPinnedOnly && visibleLiveCount == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateFilterPillVisual(Button? btn, bool active)
    {
        if (btn == null) return;
        btn.Background = active
            ? (TryFindResource("DockActiveBgBrush") as Brush ?? ThemeBrushes.DockActiveBg)
            : Brushes.Transparent;
        btn.Foreground = active
            ? (TryFindResource("PrimaryTextBrush") as Brush ?? ThemeBrushes.PrimaryText)
            : (TryFindResource("SecondaryBrush") as Brush ?? ThemeBrushes.SecondaryText);
    }
    #endregion

    #region Telemetry Callbacks
    private void OnVitalsUpdated(SystemVitalsSnapshot snap)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateVitalsUI(snap));
    }

    private void OnNetworkUpdated(NetworkStatusSnapshot snap)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateNetworkUI(snap));
    }

    private void OnPowerStatusChanged(PowerStatusInfo status)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateBatteryUI(status));
    }

    private void OnMediaChanged()
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateMediaUI(Island?.MediaServiceInstance?.Current));
    }

    private void OnTrackChanged()
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateMediaUI(Island?.MediaServiceInstance?.Current));
    }

    private void OnWeatherUpdated(WeatherSnapshot snap)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(() => UpdateWeatherUI(snap));
    }

    private void OnTimerStateChanged()
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(UpdateTimerUI);
    }

    private void OnVolumeChanged(float volume, bool muted)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(UpdateAudioUI);
    }

    private void OnEndpointsChanged(IReadOnlyList<AudioEndpointInfo> _)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(UpdateAudioUI);
    }

    private void OnPetMoodChanged(PetMood _)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(UpdatePetUI);
    }

    private void OnPetAffectionChanged(int _)
    {
        if (!_isExpanded) return;
        Dispatcher.InvokeAsync(UpdatePetUI);
    }

    private void OnDesktopWidgetsChanged()
    {
        Dispatcher.InvokeAsync(UpdateDesktopPinHeaderUI);
    }
    #endregion

    #region UI Updaters
    private static void SetBarRatio(ColumnDefinition? fillCol, ColumnDefinition? trackCol, double percent, bool allowZero = false)
    {
        if (fillCol == null || trackCol == null) return;
        double minVal = allowZero ? 0.0 : 2.0;
        double clamped = Math.Clamp(percent, minVal, 100.0);
        fillCol.Width = new GridLength(clamped, GridUnitType.Star);
        trackCol.Width = new GridLength(Math.Max(0.0, 100.0 - clamped), GridUnitType.Star);
    }

    private void UpdateVitalsUI(SystemVitalsSnapshot snap)
    {
        if (CpuPercentBadge != null)
            CpuPercentBadge.Text = $"{snap.CpuPercent}%";

        if (CpuDetailText != null)
            CpuDetailText.Text = $"{snap.CpuPercent}%";

        if (RamDetailText != null)
            RamDetailText.Text = $"{snap.RamUsedGb:0.0} / {snap.RamTotalGb:0} GB";

        SetBarRatio(CpuBarFillCol, CpuBarTrackCol, snap.CpuPercent);
        SetBarRatio(RamBarFillCol, RamBarTrackCol, snap.RamPercent);
    }

    private void UpdateBatteryUI(PowerStatusInfo status)
    {
        if (status == null) return;

        if (BatteryBigPercent != null)
            BatteryBigPercent.Text = status.HasBattery ? $"{status.BatteryPercent}%" : "AC";

        if (BatteryStatusText != null)
        {
            if (status.IsCharging)
                BatteryStatusText.Text = "Charging";
            else if (status.IsAcOnline)
                BatteryStatusText.Text = "Plugged in";
            else
                BatteryStatusText.Text = "On battery";
        }

        if (PowerPlanBadge != null)
        {
            string plan = string.IsNullOrWhiteSpace(status.ActiveSchemeName) ? "Balanced" : status.ActiveSchemeName;
            PowerPlanBadge.Text = plan;
        }

        int pct = status.HasBattery ? status.BatteryPercent : 100;
        SetBarRatio(BatteryBarFillCol, BatteryBarTrackCol, pct);
        if (BatteryBarFill != null)
        {
            BatteryBarFill.Background = pct <= 15
                ? ThemeBrushes.Red
                : (pct <= 35 ? ThemeBrushes.Amber : ThemeBrushes.Green);
        }
    }

    private void UpdateNetworkUI(NetworkStatusSnapshot snap)
    {
        if (snap == null) return;

        if (NetDownloadSpeed != null)
            NetDownloadSpeed.Text = snap.DownloadSpeedText;

        if (NetUploadSpeed != null)
            NetUploadSpeed.Text = snap.UploadSpeedText;

        if (NetworkNameText != null)
        {
            string name = !string.IsNullOrWhiteSpace(snap.ConnectionName)
                ? snap.ConnectionName
                : (snap.IsConnected ? "Connected" : "Disconnected");
            NetworkNameText.Text = $"{name} · {snap.LocalIpv4}";
        }

        if (NetworkPingBadge != null)
        {
            NetworkPingBadge.Text = snap.PingMs > 0 ? $"{snap.PingMs} ms" : (snap.IsConnected ? "Active" : "Offline");
        }
    }

    private void UpdateMediaUI(MediaInfo? media)
    {
        bool hasMedia = media != null && !string.IsNullOrWhiteSpace(media.Title);

        if (TrackTitleText != null)
            TrackTitleText.Text = hasMedia ? media!.Title : "No media playing";

        if (TrackArtistText != null)
            TrackArtistText.Text = hasMedia ? (string.IsNullOrWhiteSpace(media!.Artist) ? "Unknown Artist" : media.Artist) : "Open Spotify, Apple Music, or YouTube Music";

        if (PlayPauseMiniGlyph != null)
            PlayPauseMiniGlyph.Text = (media?.IsPlaying ?? false) ? "\uE769" : "\uE768";

        if (PlayerBrandBadgeHost != null && PlayerBrandBadgeText != null)
        {
            string brand = media?.BrandInfo?.DisplayName ?? "";
            if (!string.IsNullOrWhiteSpace(brand))
            {
                PlayerBrandBadgeText.Text = brand;
                PlayerBrandBadgeHost.Visibility = Visibility.Visible;
            }
            else
            {
                PlayerBrandBadgeHost.Visibility = Visibility.Collapsed;
            }
        }

        MediaArtDisc?.SetArtwork(media?.Artwork);
    }

    internal void ApplyAlbumArtStyle(string? style)
    {
        MediaArtDisc?.SetStyle(style);
    }

    internal bool StepMediaDisc(double dt, bool isPlaying, float bassLevel, bool reducedMotion) =>
        MediaArtDisc?.StepPhysics(dt, isPlaying, bassLevel, reducedMotion) ?? true;

    private void UpdateCalendarUI()
    {
        if (TodayDateBadge != null)
        {
            TodayDateBadge.Text = DateTime.Now.ToString("ddd, MMM d");
        }

        var island = Island;
        if (island?.CalendarServiceInstance is { } cal)
        {
            if (cal.NextEvent is { } next)
            {
                if (NextEventTitleText != null) NextEventTitleText.Text = next.Subject;
                if (NextEventTimeText != null) NextEventTimeText.Text = next.TimeBadgeText;
                return;
            }
        }

        if (NextEventTitleText != null) NextEventTitleText.Text = "All caught up";
        if (NextEventTimeText != null) NextEventTimeText.Text = "No upcoming meetings today";
    }

    private void UpdateNotesUI()
    {
        var island = Island;
        if (island?.NotesServiceInstance is { } notes)
        {
            if (NotesCountBadge != null)
            {
                int count = notes.Notes.Count;
                NotesCountBadge.Text = count == 1 ? "1 note" : $"{count} notes";
            }

            if (NotesSnippetText != null)
            {
                var note = notes.Notes.Count > 0 ? notes.Notes[0] : null;
                if (note != null)
                {
                    string text = !string.IsNullOrWhiteSpace(note.Title) ? note.Title : (note.Content ?? "");
                    string[] lines = text.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    string firstLine = lines.Length > 0 ? lines[0] : "Empty Note";
                    NotesSnippetText.Text = firstLine.Length > 28 ? firstLine.Substring(0, 28) + "..." : firstLine;
                }
                else
                {
                    NotesSnippetText.Text = "Scratchpad & Checklist";
                }
            }
        }
    }

    private void UpdateShelfUI()
    {
        var island = Island;
        if (island?.DropShelfServiceInstance is { } shelf && ShelfCountBadge != null)
        {
            ShelfCountBadge.Text = shelf.Count == 1 ? "1 file" : $"{shelf.Count} files";
            if (ShelfStatusText != null)
                ShelfStatusText.Text = shelf.Count > 0 ? $"{shelf.Count} items staged" : "Stage files to drag, ZIP, or share";
        }
    }

    private void UpdateWeatherUI(WeatherSnapshot? weather)
    {
        if (weather != null)
        {
            if (WeatherGlyphIcon != null) WeatherGlyphIcon.Text = weather.Glyph;
            if (WeatherLocationBadge != null) WeatherLocationBadge.Text = string.IsNullOrWhiteSpace(weather.LocationName) ? "Local" : weather.LocationName;
            if (WeatherTempBigText != null) WeatherTempBigText.Text = weather.BadgeText;
            if (WeatherConditionText != null) WeatherConditionText.Text = weather.Condition;
            if (WeatherForecastText != null) WeatherForecastText.Text = weather.SummaryLine;
        }
        else
        {
            if (WeatherTempBigText != null) WeatherTempBigText.Text = "—°C";
            if (WeatherConditionText != null) WeatherConditionText.Text = "Local";
            if (WeatherForecastText != null) WeatherForecastText.Text = "Updating local forecast";
        }
    }

    private void UpdateTimerUI()
    {
        var timer = Island?.CountdownServiceInstance;
        if (timer != null)
        {
            if (TimerBigReadoutText != null) TimerBigReadoutText.Text = timer.FormatRemaining();
            if (TimerModeBadgeText != null) TimerModeBadgeText.Text = timer.IsRunning ? "Running" : (timer.IsActive ? "Paused" : "Ready");
            if (TimerQuickToggleGlyph != null) TimerQuickToggleGlyph.Text = timer.IsRunning ? "\uE769" : "\uE768";
            if (TimerStatusSubtitleText != null)
            {
                TimerStatusSubtitleText.Text = timer.IsRunning
                    ? $"{timer.Label} active"
                    : (timer.IsActive ? $"{timer.Label} paused" : "Pomodoro 25m");
            }
        }
        else
        {
            if (TimerBigReadoutText != null) TimerBigReadoutText.Text = "25:00";
            if (TimerModeBadgeText != null) TimerModeBadgeText.Text = "Ready";
        }
    }

    private void UpdateStorageUI()
    {
        var drive = DesktopWidgetWindow.ReadSystemDriveSnapshot();
        if (StorageFreeBadgeText != null)
            StorageFreeBadgeText.Text = $"{drive.FreeGb:0} GB free";
        if (StorageUsedDetailText != null)
            StorageUsedDetailText.Text = $"{drive.UsedGb:0.0} / {drive.TotalGb:0.0} GB";
        SetBarRatio(StorageBarFillCol, StorageBarTrackCol, drive.UsedPercent);
        if (StorageDriveSummaryText != null)
            StorageDriveSummaryText.Text = $"{drive.DriveLabel} · {drive.UsedPercent:0}% used ({drive.DriveFormat})";
    }

    private void UpdateAudioUI()
    {
        var island = Island;
        var volSvc = island?.VolumeServiceInstance;
        var epSvc = island?.AudioEndpointServiceInstance;

        float vol = volSvc?.CurrentVolume ?? 0.5f;
        bool muted = volSvc?.IsMuted ?? false;
        int pct = (int)Math.Round(Math.Clamp(vol, 0f, 1f) * 100f);

        if (AudioVolumeBadgeText != null)
            AudioVolumeBadgeText.Text = muted ? "Muted" : $"{pct}%";

        SetBarRatio(AudioBarFillCol, AudioBarTrackCol, muted ? 0.0 : pct, allowZero: true);

        if (AudioQuickMuteGlyph != null)
            AudioQuickMuteGlyph.Text = muted ? "\uE74F" : "\uE767";

        var currentEp = epSvc?.CurrentDefault;
        if (AudioDeviceNameText != null)
            AudioDeviceNameText.Text = currentEp != null ? currentEp.Name : "Default Audio Output";

        if (AudioEndpointSubtitleText != null)
        {
            int count = epSvc?.Endpoints.Count ?? 0;
            AudioEndpointSubtitleText.Text = count > 1
                ? $"{count} outputs available"
                : "Output & app mixer";
        }
    }

    private void UpdatePetUI()
    {
        var pet = Island?.PetService;
        if (pet != null)
        {
            if (PetMoodBadgeText != null) PetMoodBadgeText.Text = pet.CurrentMood.ToString();
            if (PetNameAndSpeciesText != null) PetNameAndSpeciesText.Text = $"{pet.PetName} · {pet.PetSpecies}";
            if (PetAffectionDetailText != null) PetAffectionDetailText.Text = $"Affection {pet.AffectionScore} · {pet.TreatCount} treats";
        }
    }

    private void UpdateMechSoundsUI()
    {
        if (MechSoundsStateBadge == null) return;
        bool enabled = Island?.KeyboardSoundServiceInstance?.IsEnabled ?? false;
        MechSoundsStateBadge.Text = enabled ? "Active" : "Off";
    }

    private void UpdateDesktopPinHeaderUI()
    {
        int activeCount = DesktopWidgetWindow.ActiveCount;
        if (UnpinAllDesktopBtn != null)
        {
            UnpinAllDesktopBtn.Visibility = activeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (UnpinAllDesktopText != null && activeCount > 0)
        {
            UnpinAllDesktopText.Text = activeCount == 1 ? "Unpin (1)" : $"Unpin ({activeCount})";
        }
        if (FilterPinnedText != null)
        {
            FilterPinnedText.Text = activeCount > 0 ? $"Pinned ({activeCount})" : "Pinned";
        }

        UpdatePinBtnState(PinVitalsBtn, DesktopWidgetKind.Vitals);
        UpdatePinBtnState(PinBatteryBtn, DesktopWidgetKind.Battery);
        UpdatePinBtnState(PinNetworkBtn, DesktopWidgetKind.Network);
        UpdatePinBtnState(PinCalendarBtn, DesktopWidgetKind.Calendar);
        UpdatePinBtnState(PinWeatherBtn, DesktopWidgetKind.Weather);
        UpdatePinBtnState(PinTimerBtn, DesktopWidgetKind.Timer);
        UpdatePinBtnState(PinStorageBtn, DesktopWidgetKind.Storage);
        UpdatePinBtnState(PinAudioBtn, DesktopWidgetKind.Audio);
        UpdatePinBtnState(PinMediaBtn, DesktopWidgetKind.Media);

        if (_activeFilter == "Pinned")
        {
            ApplyFilter("Pinned");
        }
    }

    private static void UpdatePinBtnState(Button? btn, DesktopWidgetKind kind)
    {
        if (btn == null) return;
        bool pinned = DesktopWidgetWindow.IsPinned(kind);
        btn.Foreground = pinned ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;
        btn.Background = pinned ? PinnedBtnBgBrush : Brushes.Transparent;
        ToolTipService.SetToolTip(btn, pinned ? "Pinned to Desktop — click to unpin" : "Pin floating widget to Desktop");
    }
    #endregion

    #region Navigation Handlers
    private void SystemVitalsCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.SystemHud);

    private void BatteryCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Battery);

    private void NetworkCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Network);

    private void CalendarCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Calendar);

    private void PeripheralsCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Peripherals);

    private void NotesCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Notes);

    private void CalcCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Calc);

    private void ShelfCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Shelf);

    private void VoiceMemoCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.VoiceMemo);

    private void CaptureStudioCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.CaptureStudio);

    private void ConverterCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Converter);

    private void TermiCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Termi);

    private void PhoneBridgeCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.PhoneBridge);

    private void MechSoundsCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.MechSounds);

    private void ClipboardCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Clipboard);

    private void MirrorCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Mirror);

    private void MediaCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Media);

    private void WeatherCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (Island?.WeatherServiceInstance is { } weather)
        {
            _ = weather.RefreshAsync();
            Island.ShowToast(weather.Current.Glyph, "Weather Synced", $"{weather.Current.LocationName} · {weather.Current.BadgeText}", ThemeBrushes.AmberColor, "weather");
        }
    }

    private void TimerCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Media);

    private void TimerQuickToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        Island?.CountdownServiceInstance?.TogglePause();
        UpdateTimerUI();
        e.Handled = true;
    }

    private void AudioQuickMuteBtn_Click(object sender, RoutedEventArgs e)
    {
        Island?.VolumeServiceInstance?.ToggleMute();
        UpdateAudioUI();
        e.Handled = true;
    }

    private void StorageCard_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", root) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.LogDebug("WidgetsGallery", ex);
        }
    }

    private void AudioCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Audio);

    private void PetCard_Click(object sender, MouseButtonEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Pet);

    private void PinDesktopWidget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string rawKind } &&
            Enum.TryParse<DesktopWidgetKind>(rawKind, ignoreCase: true, out var kind))
        {
            DesktopWidgetWindow.Toggle(kind, Island);
            UpdateDesktopPinHeaderUI();
        }
        e.Handled = true;
    }

    private void UnpinAllDesktopBtn_Click(object sender, RoutedEventArgs e)
    {
        DesktopWidgetWindow.CloseAll();
        UpdateDesktopPinHeaderUI();
        e.Handled = true;
    }

    private void OpenLaunchpad_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(ExpandedView.Launcher);

    private void PrevMiniBtn_Click(object sender, RoutedEventArgs e)
    {
        Island?.MediaServiceInstance?.Previous();
        e.Handled = true;
    }

    private void PlayPauseMiniBtn_Click(object sender, RoutedEventArgs e)
    {
        Island?.MediaServiceInstance?.TogglePlayPause();
        e.Handled = true;
    }

    private void NextMiniBtn_Click(object sender, RoutedEventArgs e)
    {
        Island?.MediaServiceInstance?.Next();
        e.Handled = true;
    }

    public void Scroll(double delta)
    {
        if (WidgetsScrollViewer != null)
        {
            double target = Math.Clamp(WidgetsScrollViewer.VerticalOffset + delta, 0, Math.Max(0, WidgetsScrollViewer.ScrollableHeight));
            WidgetsScrollViewer.ScrollToVerticalOffset(target);
        }
    }

    private void WidgetsGallery_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (WidgetsScrollViewer != null)
        {
            double step = e.Delta > 0 ? -64.0 : 64.0;
            double target = Math.Clamp(WidgetsScrollViewer.VerticalOffset + step, 0, Math.Max(0, WidgetsScrollViewer.ScrollableHeight));
            WidgetsScrollViewer.ScrollToVerticalOffset(target);
            e.Handled = true;
        }
    }

    private void NewDropletTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = AppPaths.DropletsDirectory;
            Directory.CreateDirectory(dir);
            string samplePath = Path.Combine(dir, "sample-status.droplet.json");
            if (!File.Exists(samplePath))
            {
                string json = """
{
  "id": "sample_status",
  "name": "Sample Status Droplet",
  "description": "Starter community droplet template",
  "glyph": "\uE943",
  "pollIntervalSeconds": 60,
  "endpointUrl": "",
  "enabledByDefault": false
}
""";
                File.WriteAllText(samplePath, json);
            }

            int loaded = Island?.CommunityDroplets?.ScanAndLoadAll(dir) ?? 0;
            Island?.PushNotificationToast("\uE943", "Community Droplets", $"Template ready · {loaded} droplet(s) loaded", ThemeBrushes.GreenColor);
        }
        catch (Exception ex)
        {
            Island?.PushNotificationToast("\uE783", "Droplet Template Error", ex.Message, ThemeBrushes.AmberColor);
        }
    }
    #endregion
}
