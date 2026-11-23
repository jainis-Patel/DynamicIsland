using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using DynamicIsland.Core;
using DynamicIsland.Views.Widgets;

namespace DynamicIsland;

public partial class IslandWindow
{
    // -------------------------------------------------------------------------
    //  Media View Forwarders (MediaContainer)
    // -------------------------------------------------------------------------
    internal Border ClockCalendarBadge => MediaContainer.ClockCalendarBadge;
    internal TextBlock ClockCalendarText => MediaContainer.ClockCalendarText;
    internal TextBlock ClockCpuText => MediaContainer.ClockCpuText;
    internal System.Windows.Controls.Border ClockNetSpeedBadge => MediaContainer.ClockNetSpeedBadge;
    internal System.Windows.Controls.TextBlock ClockNetSpeedText => MediaContainer.ClockNetSpeedText;
    internal TextBlock ClockRamText => MediaContainer.ClockRamText;
    internal Border ClockWeatherBadge => MediaContainer.ClockWeatherBadge;
    internal TextBlock ClockWeatherGlyph => MediaContainer.ClockWeatherGlyph;
    internal TextBlock ClockWeatherText => MediaContainer.ClockWeatherText;
    internal Border ClockWifiBadge => MediaContainer.ClockWifiBadge;
    internal TextBlock ClockWifiGlyph => MediaContainer.ClockWifiGlyph;
    internal TextBlock ClockWifiText => MediaContainer.ClockWifiText;
    internal TextBlock CpuText => MediaContainer.CpuText;
    internal TextBlock DurationText => MediaContainer.DurationText;
    internal TextBlock ExpandedArtFallback => MediaContainer.ExpandedArtFallback;
    internal Image ExpandedArtImage => MediaContainer.ExpandedArtImage;
    internal TextBlock ExpandedArtist => MediaContainer.ExpandedArtist;
    internal TextBlock ExpandedBigClock => MediaContainer.ExpandedBigClock;
    internal TextBlock ExpandedBigDate => MediaContainer.ExpandedBigDate;
    internal Border ExpandedCallCamBadge => MediaContainer.ExpandedCallCamBadge;
    internal Grid ExpandedCallHeader => MediaContainer.ExpandedCallHeader;
    internal Border ExpandedCallMicBadge => MediaContainer.ExpandedCallMicBadge;
    internal TextBlock ExpandedCallMicText => MediaContainer.ExpandedCallMicText;
    internal TextBlock ExpandedCallStatus => MediaContainer.ExpandedCallStatus;
    internal TextBlock ExpandedCallTimer => MediaContainer.ExpandedCallTimer;
    internal TextBlock ExpandedCallTitle => MediaContainer.ExpandedCallTitle;
    internal Grid ExpandedClockHeader => MediaContainer.ExpandedClockHeader;
    internal TextBlock ExpandedDate => MediaContainer.ExpandedDate;
    internal Grid ExpandedMediaHeader => MediaContainer.ExpandedMediaHeader;
    internal TextBlock ExpandedMiniClock => MediaContainer.ExpandedMiniClock;
    internal TextBlock ExpandedSourceApp => MediaContainer.ExpandedSourceApp;
    internal Viewbox ExpandedSourceLogoBox => MediaContainer.ExpandedSourceLogoBox;
    internal System.Windows.Shapes.Path ExpandedSourceLogoPath => MediaContainer.ExpandedSourceLogoPath;
    internal TextBlock ExpandedTimer => MediaContainer.ExpandedTimer;
    internal TextBlock ExpandedTimerModeLabel => MediaContainer.ExpandedTimerModeLabel;
    internal TextBlock ExpandedTitle => MediaContainer.ExpandedTitle;
    internal System.Windows.Controls.Grid KaraokeLyricsHost => MediaContainer.KaraokeLyricsHost;
    internal System.Windows.Controls.TextBlock LyricCurrentLine => MediaContainer.LyricCurrentLine;
    internal System.Windows.Controls.TextBlock LyricNextLine => MediaContainer.LyricNextLine;
    internal System.Windows.Controls.TextBlock LyricPrevLine => MediaContainer.LyricPrevLine;
    internal System.Windows.Controls.Border MediaLyricsBadge => MediaContainer.MediaLyricsBadge;
    internal System.Windows.Controls.TextBlock MediaLyricsBadgeText => MediaContainer.MediaLyricsBadgeText;
    internal System.Windows.Controls.TextBlock MediaLyricsIcon => MediaContainer.MediaLyricsIcon;
    internal System.Windows.Controls.Border MediaVisualizerBadge => MediaContainer.MediaVisualizerBadge;
    internal System.Windows.Controls.Grid MediaTransportControlsGrid => MediaContainer.MediaTransportControlsGrid;
    internal System.Windows.Controls.TextBlock MediaVisualizerBadgeText => MediaContainer.MediaVisualizerBadgeText;
    internal Button NextButton => MediaContainer.NextButton;
    internal Button PlayPauseButton => MediaContainer.PlayPauseButton;
    internal TextBlock PlayPauseGlyph => MediaContainer.PlayPauseGlyph;
    internal Button PomodoroButton => MediaContainer.PomodoroButton;
    internal TextBlock PomodoroButtonText => MediaContainer.PomodoroButtonText;
    internal TextBlock PositionText => MediaContainer.PositionText;
    internal Button PrevButton => MediaContainer.PrevButton;
    internal TextBlock RamText => MediaContainer.RamText;
    internal Button ResetButton => MediaContainer.ResetButton;
    internal Button StartPauseButton => MediaContainer.StartPauseButton;
    internal TextBlock StartPauseGlyph => MediaContainer.StartPauseGlyph;
    internal TextBlock StartPauseLabel => MediaContainer.StartPauseLabel;
    internal Slider TimelineSlider => MediaContainer.TimelineSlider;

    // -------------------------------------------------------------------------
    //  Shelf View Forwarders (ShelfContainer)
    // -------------------------------------------------------------------------
    internal Button ClearShelfButton => ShelfContainer.ClearShelfButton;
    internal System.Windows.Controls.Button CopyAllShelfButton => ShelfContainer.CopyAllShelfButton;
    internal System.Windows.Controls.Border DragAllShelfBtn => ShelfContainer.DragAllShelfBtn;
    internal System.Windows.Controls.Border ShelfCountBadge => ShelfContainer.ShelfCountBadge;
    internal TextBlock ShelfCountText => ShelfContainer.ShelfCountText;
    internal FrameworkElement ShelfEmptyPlaceholder => ShelfContainer.ShelfEmptyPlaceholder;
    internal ItemsControl ShelfItemsControl => ShelfContainer.ShelfItemsControl;
    internal System.Windows.Controls.Button ShelfScrollLeftBtn => ShelfContainer.ShelfScrollLeftBtn;
    internal System.Windows.Controls.Button ShelfScrollRightBtn => ShelfContainer.ShelfScrollRightBtn;
    internal System.Windows.Controls.ScrollViewer ShelfScrollViewer => ShelfContainer.ShelfScrollViewer;
    internal Button ZipShelfButton => ShelfContainer.ZipShelfButton;
    internal Button WindowsShareButton => ShelfContainer.WindowsShareButton;
    internal Button PhoneShareButton => ShelfContainer.PhoneShareButton;
    internal Button ShelfActionsButton => ShelfContainer.ShelfActionsButton;
    internal Button SummonBasketButton => ShelfContainer.SummonBasketButton;
    internal FrameworkElement? ShelfItemActionsGroup => ShelfContainer.ShelfItemActionsGroup;

    // -------------------------------------------------------------------------
    //  Clipboard View Forwarders (ClipboardContainer)
    // -------------------------------------------------------------------------
    internal Button ClearClipboardButton => ClipboardContainer.ClearClipboardButton;
    internal Button CaptureRegionButton => ClipboardContainer.CaptureRegionButton;
    internal Button ClipboardQuickPickerButton => ClipboardContainer.ClipboardQuickPickerButton;
    internal TextBlock ClipboardQueueBtnText => ClipboardContainer.ClipboardQueueBtnText;
    internal System.Windows.Controls.Button ClearClipboardSearchBtn => ClipboardContainer.ClearClipboardSearchBtn;
    internal TextBlock ClipboardCountText => ClipboardContainer.ClipboardCountText;
    internal FrameworkElement ClipboardEmptyPlaceholder => ClipboardContainer.ClipboardEmptyPlaceholder;
    internal ItemsControl ClipboardItemsControl => ClipboardContainer.ClipboardItemsControl;
    internal System.Windows.Controls.Button ClipboardScrollLeftBtn => ClipboardContainer.ClipboardScrollLeftBtn;
    internal System.Windows.Controls.Button ClipboardScrollRightBtn => ClipboardContainer.ClipboardScrollRightBtn;
    internal System.Windows.Controls.ScrollViewer ClipboardScrollViewer => ClipboardContainer.ClipboardScrollViewer;
    internal System.Windows.Controls.TextBox ClipboardSearchBox => ClipboardContainer.ClipboardSearchBox;
    internal System.Windows.Controls.Button ClipFilterAllBtn => ClipboardContainer.ClipFilterAllBtn;
    internal System.Windows.Controls.Button ClipFilterCodeBtn => ClipboardContainer.ClipFilterCodeBtn;
    internal System.Windows.Controls.Button ClipFilterColorsBtn => ClipboardContainer.ClipFilterColorsBtn;
    internal System.Windows.Controls.Button ClipFilterImagesBtn => ClipboardContainer.ClipFilterImagesBtn;
    internal System.Windows.Controls.Button ClipFilterLinksBtn => ClipboardContainer.ClipFilterLinksBtn;
    internal System.Windows.Controls.Button ClipFilterPinnedBtn => ClipboardContainer.ClipFilterPinnedBtn;
    internal Button EyedropperButton => ClipboardContainer.EyedropperButton;
    internal Button OcrClipboardButton => ClipboardContainer.OcrClipboardButton;

    // -------------------------------------------------------------------------
    //  Calc View Forwarders (CalcContainer)
    // -------------------------------------------------------------------------
    internal TextBlock CalcExplanationText => CalcContainer.CalcExplanationText;
    internal ItemsControl CalcHistoryItemsControl => CalcContainer.CalcHistoryItemsControl;
    internal TextBox CalcInputBox => CalcContainer.CalcInputBox;
    internal TextBlock CalcPlaceholder => CalcContainer.CalcPlaceholder;
    internal Border CalcResultHost => CalcContainer.CalcResultHost;
    internal TextBlock CalcResultText => CalcContainer.CalcResultText;

    // -------------------------------------------------------------------------
    //  Notes View Forwarders (NotesContainer)
    // -------------------------------------------------------------------------
    internal TextBox NoteEditorBox => NotesContainer.NoteEditorBox;
    internal ItemsControl NotesListControl => NotesContainer.NotesListControl;

    // -------------------------------------------------------------------------
    //  Settings View Forwarders (Minimal cross-partial accessor)
    // -------------------------------------------------------------------------
    internal DynamicIsland.Core.VirtualPetControl SettingsHeroPet => SettingsContainer.PetPanel.SettingsHeroPet;

    // -------------------------------------------------------------------------
    //  Audio View Forwarders (AudioContainer)
    // -------------------------------------------------------------------------
    internal Border AudioAppMixerEmptyPlaceholder => AudioContainer.AudioAppMixerEmptyPlaceholder;
    internal ScrollViewer AudioAppMixerScrollViewer => AudioContainer.AudioAppMixerScrollViewer;
    internal ItemsControl AudioAppSessionsControl => AudioContainer.AudioAppSessionsControl;
    internal ItemsControl AudioEndpointsControl => AudioContainer.AudioEndpointsControl;
    internal ScrollViewer AudioEndpointsScrollViewer => AudioContainer.AudioEndpointsScrollViewer;
    internal TextBlock AudioSectionTitle => AudioContainer.AudioSectionTitle;
    internal Button AudioModeOutputsBtn => AudioContainer.AudioModeOutputsBtn;
    internal Button AudioModeMixerBtn => AudioContainer.AudioModeMixerBtn;
    internal Button HardwareMicBtn => AudioContainer.HardwareMicBtn;
    internal TextBlock HardwareMicGlyph => AudioContainer.HardwareMicGlyph;
    internal TextBlock HardwareMicText => AudioContainer.HardwareMicText;
    internal TextBlock HeroDeviceGlyph => AudioContainer.HeroDeviceGlyph;
    internal TextBlock HeroDeviceNameText => AudioContainer.HeroDeviceNameText;
    internal TextBlock HeroDeviceTypeText => AudioContainer.HeroDeviceTypeText;
    internal Border HeroDeviceBatteryBadge => AudioContainer.HeroDeviceBatteryBadge;
    internal TextBlock HeroDeviceBatteryText => AudioContainer.HeroDeviceBatteryText;
    internal Button HeroMasterMuteBtn => AudioContainer.HeroMasterMuteBtn;
    internal TextBlock HeroMasterMuteGlyph => AudioContainer.HeroMasterMuteGlyph;
    internal TextBlock HeroSpeakerIcon => AudioContainer.HeroSpeakerIcon;
    internal Slider HeroMasterVolumeSlider => AudioContainer.HeroMasterVolumeSlider;
    internal TextBlock HeroMasterVolumePercentText => AudioContainer.HeroMasterVolumePercentText;

    // -------------------------------------------------------------------------
    //  Battery View Forwarders (BatteryContainer)
    // -------------------------------------------------------------------------
    internal TextBlock BatteryChargingBolt => BatteryContainer.BatteryChargingBolt;
    internal Border BatteryHeaderBadge => BatteryContainer.BatteryHeaderBadge;
    internal TextBlock BatteryHeaderBadgeText => BatteryContainer.BatteryHeaderBadgeText;
    internal System.Windows.Shapes.Ellipse BatteryHeaderDot => BatteryContainer.BatteryHeaderDot;
    internal System.Windows.Controls.TextBlock BatteryHealthText => BatteryContainer.BatteryHealthText;
    internal TextBlock BatteryHeroPercentText => BatteryContainer.BatteryHeroPercentText;
    internal Border BatteryLevelFill => BatteryContainer.BatteryLevelFill;
    internal TextBlock BatteryStatusSubtitle => BatteryContainer.BatteryStatusSubtitle;
    internal TextBlock BatteryTimeEstimate => BatteryContainer.BatteryTimeEstimate;
    internal System.Windows.Controls.Border BatteryWattageBadge => BatteryContainer.BatteryWattageBadge;
    internal System.Windows.Controls.TextBlock BatteryWattageText => BatteryContainer.BatteryWattageText;
    internal Slider BrightnessSlider => BatteryContainer.BrightnessSlider;
    internal TextBlock BrightnessValueText => BatteryContainer.BrightnessValueText;
    internal Button CaffeineQuickBtn => BatteryContainer.CaffeineQuickBtn;
    internal TextBlock CaffeineQuickText => BatteryContainer.CaffeineQuickText;
    internal System.Windows.Controls.Border DisplayRefreshBadge => BatteryContainer.DisplayRefreshBadge;
    internal System.Windows.Controls.TextBlock DisplayRefreshText => BatteryContainer.DisplayRefreshText;
    internal System.Windows.Controls.TextBlock DisplayResolutionText => BatteryContainer.DisplayResolutionText;
    internal System.Windows.Controls.Button NightLightQuickBtn => BatteryContainer.NightLightQuickBtn;
    internal System.Windows.Controls.TextBlock NightLightQuickIcon => BatteryContainer.NightLightQuickIcon;
    internal System.Windows.Controls.TextBlock NightLightQuickText => BatteryContainer.NightLightQuickText;
    internal Border PlanBalancedCard => BatteryContainer.PlanBalancedCard;
    internal System.Windows.Controls.TextBlock PlanBalancedIcon => BatteryContainer.PlanBalancedIcon;
    internal System.Windows.Controls.TextBlock PlanBalancedSub => BatteryContainer.PlanBalancedSub;
    internal System.Windows.Controls.TextBlock PlanBalancedTitle => BatteryContainer.PlanBalancedTitle;
    internal Border PlanPerfCard => BatteryContainer.PlanPerfCard;
    internal System.Windows.Controls.TextBlock PlanPerfIcon => BatteryContainer.PlanPerfIcon;
    internal System.Windows.Controls.TextBlock PlanPerfSub => BatteryContainer.PlanPerfSub;
    internal System.Windows.Controls.TextBlock PlanPerfTitle => BatteryContainer.PlanPerfTitle;
    internal Border PlanSaverCard => BatteryContainer.PlanSaverCard;
    internal System.Windows.Controls.TextBlock PlanSaverIcon => BatteryContainer.PlanSaverIcon;
    internal System.Windows.Controls.TextBlock PlanSaverSub => BatteryContainer.PlanSaverSub;
    internal System.Windows.Controls.TextBlock PlanSaverTitle => BatteryContainer.PlanSaverTitle;

    // -------------------------------------------------------------------------
    //  VoiceMemo View Forwarders (VoiceMemoContainer)
    // -------------------------------------------------------------------------
    internal Button HeroRecordBtn => VoiceMemoContainer.HeroRecordBtn;
    internal TextBlock HeroRecordGlyph => VoiceMemoContainer.HeroRecordGlyph;
    internal TextBlock HeroRecordStatus => VoiceMemoContainer.HeroRecordStatus;
    internal TextBlock HeroRecordTimer => VoiceMemoContainer.HeroRecordTimer;
    internal TextBlock LastMemoDuration => VoiceMemoContainer.LastMemoDuration;
    internal TextBlock LastMemoFileName => VoiceMemoContainer.LastMemoFileName;
    internal StackPanel LastMemoHost => VoiceMemoContainer.LastMemoHost;
    internal TextBlock MemoPlayGlyph => VoiceMemoContainer.MemoPlayGlyph;
    internal TextBlock MemoPlayLabel => VoiceMemoContainer.MemoPlayLabel;
    internal Button MemoPlayPauseBtn => VoiceMemoContainer.MemoPlayPauseBtn;
    internal StackPanel NoMemoPlaceholder => VoiceMemoContainer.NoMemoPlaceholder;
    internal System.Windows.Shapes.Rectangle RecBar0 => VoiceMemoContainer.RecBar0;
    internal System.Windows.Shapes.Rectangle RecBar1 => VoiceMemoContainer.RecBar1;
    internal System.Windows.Shapes.Rectangle RecBar2 => VoiceMemoContainer.RecBar2;
    internal System.Windows.Shapes.Rectangle RecBar3 => VoiceMemoContainer.RecBar3;
    internal System.Windows.Shapes.Rectangle RecBar4 => VoiceMemoContainer.RecBar4;
    internal Border RecordHalo => VoiceMemoContainer.RecordHalo;
    internal ItemsControl VoiceMemosItemsControl => VoiceMemoContainer.VoiceMemosItemsControl;
    internal ScrollViewer VoiceMemosScrollViewer => VoiceMemoContainer.VoiceMemosScrollViewer;

    // -------------------------------------------------------------------------
    //  Peripherals View Forwarders (PeripheralsContainer)
    // -------------------------------------------------------------------------
    internal Button OpenBluetoothSettingsBtn => PeripheralsContainer.OpenBluetoothSettingsBtn;
    internal StackPanel PeripheralsEmptyPlaceholder => PeripheralsContainer.PeripheralsEmptyPlaceholder;
    internal Button PeripheralsFilterBtn => PeripheralsContainer.PeripheralsFilterBtn;
    internal TextBlock PeripheralsFilterText => PeripheralsContainer.PeripheralsFilterText;
    internal ItemsControl PeripheralsItemsControl => PeripheralsContainer.PeripheralsItemsControl;
    internal System.Windows.Controls.ScrollViewer PeripheralsScrollViewer => PeripheralsContainer.PeripheralsScrollViewer;
    internal Button RefreshPeripheralsBtn => PeripheralsContainer.RefreshPeripheralsBtn;

    // -------------------------------------------------------------------------
    //  Network View Forwarders (NetworkContainer)
    // -------------------------------------------------------------------------
    internal Border BtToggleCard => NetworkContainer.BtToggleCard;
    internal TextBlock BtToggleGlyph => NetworkContainer.BtToggleGlyph;
    internal TextBlock BtToggleStateText => NetworkContainer.BtToggleStateText;
    internal Border HotspotCard => NetworkContainer.HotspotCard;
    internal TextBlock NetworkDownSpeedText => NetworkContainer.NetworkDownSpeedText;
    internal TextBlock NetworkHeroGlyph => NetworkContainer.NetworkHeroGlyph;
    internal Border NetworkHeroIconBg => NetworkContainer.NetworkHeroIconBg;
    internal TextBlock NetworkHeroName => NetworkContainer.NetworkHeroName;
    internal TextBlock NetworkHeroSubtitle => NetworkContainer.NetworkHeroSubtitle;
    internal Border NetworkIpBadge => NetworkContainer.NetworkIpBadge;
    internal TextBlock NetworkIpText => NetworkContainer.NetworkIpText;
    internal Grid NetworkOverviewHost => NetworkContainer.NetworkOverviewHost;
    internal System.Windows.Controls.Button NetworkPillSpeedToggleBtn => NetworkContainer.NetworkPillSpeedToggleBtn;
    internal System.Windows.Controls.TextBlock NetworkPillSpeedToggleText => NetworkContainer.NetworkPillSpeedToggleText;
    internal System.Windows.Shapes.Ellipse NetworkPingDot => NetworkContainer.NetworkPingDot;
    internal System.Windows.Controls.TextBlock NetworkPingText => NetworkContainer.NetworkPingText;
    internal TextBlock NetworkSectionTitle => NetworkContainer.NetworkSectionTitle;
    internal TextBlock NetworkUpSpeedText => NetworkContainer.NetworkUpSpeedText;
    internal Button OpenNetworkSettingsBtn => NetworkContainer.OpenNetworkSettingsBtn;
    internal Button ScanWifiModeBtn => NetworkContainer.ScanWifiModeBtn;
    internal TextBlock ScanWifiModeText => NetworkContainer.ScanWifiModeText;
    internal Border WifiNetworksCard => NetworkContainer.WifiNetworksCard;
    internal ItemsControl WifiNetworksItemsControl => NetworkContainer.WifiNetworksItemsControl;
    internal ScrollViewer WifiNetworksScrollViewer => NetworkContainer.WifiNetworksScrollViewer;
    internal Grid WifiScannerHost => NetworkContainer.WifiScannerHost;
    internal Border WifiScannerStatusPlaceholder => NetworkContainer.WifiScannerStatusPlaceholder;
    internal TextBlock WifiScannerStatusSubtitle => NetworkContainer.WifiScannerStatusSubtitle;
    internal TextBlock WifiScannerStatusTitle => NetworkContainer.WifiScannerStatusTitle;
    internal Border WifiToggleCard => NetworkContainer.WifiToggleCard;
    internal TextBlock WifiToggleGlyph => NetworkContainer.WifiToggleGlyph;
    internal TextBlock WifiToggleStateText => NetworkContainer.WifiToggleStateText;

    // -------------------------------------------------------------------------
    //  Notifications View Forwarders (NotificationsContainer)
    // -------------------------------------------------------------------------
    internal Button ClearNotificationsButton => NotificationsContainer.ClearNotificationsButton;
    internal System.Windows.Controls.Border NotificationsCountBadge => NotificationsContainer.NotificationsCountBadge;
    internal TextBlock NotificationsCountText => NotificationsContainer.NotificationsCountText;
    internal Button NotificationsDndButton => NotificationsContainer.NotificationsDndButton;
    internal TextBlock NotificationsDndGlyph => NotificationsContainer.NotificationsDndGlyph;
    internal TextBlock NotificationsDndText => NotificationsContainer.NotificationsDndText;
    internal DockPanel NotificationsFilterBar => NotificationsContainer.NotificationsFilterBar;
    internal Button NotifFilterAllBtn => NotificationsContainer.NotifFilterAllBtn;
    internal Button NotifFilterMessagesBtn => NotificationsContainer.NotifFilterMessagesBtn;
    internal TextBox NotificationsSearchBox => NotificationsContainer.NotificationsSearchBox;
    internal Button ClearNotificationsSearchBtn => NotificationsContainer.ClearNotificationsSearchBtn;
    internal StackPanel NotificationsEmptyPlaceholder => NotificationsContainer.NotificationsEmptyPlaceholder;
    internal TextBlock NotificationsEmptyGlyph => NotificationsContainer.NotificationsEmptyGlyph;
    internal TextBlock NotificationsEmptyTitle => NotificationsContainer.NotificationsEmptyTitle;
    internal TextBlock NotificationsEmptySubtitle => NotificationsContainer.NotificationsEmptySubtitle;
    internal Button NotificationsEmptyActionBtn => NotificationsContainer.NotificationsEmptyActionBtn;
    internal TextBlock NotificationsEmptyActionText => NotificationsContainer.NotificationsEmptyActionText;
    internal ItemsControl NotificationsItemsControl => NotificationsContainer.NotificationsItemsControl;
    internal System.Windows.Controls.ScrollViewer NotificationsScrollViewer => NotificationsContainer.NotificationsScrollViewer;
    internal Button UnmuteAllAppsButton => NotificationsContainer.UnmuteAllAppsButton;
    internal TextBlock UnmuteAllAppsText => NotificationsContainer.UnmuteAllAppsText;
    internal Button ToggleGroupingButton => NotificationsContainer.ToggleGroupingButton;
    internal TextBlock ToggleGroupingGlyph => NotificationsContainer.ToggleGroupingGlyph;
    internal TextBlock ToggleGroupingText => NotificationsContainer.ToggleGroupingText;

    // -------------------------------------------------------------------------
    //  SystemHud View Forwarders (SystemHudContainer)
    // -------------------------------------------------------------------------
    internal Border HudCpuBarFill => SystemHudContainer.HudCpuBarFill;
    internal TextBlock HudCpuHeroText => SystemHudContainer.HudCpuHeroText;
    internal TextBlock HudCpuStatusText => SystemHudContainer.HudCpuStatusText;
    internal StackPanel HudCpuHeatmapPanel => SystemHudContainer.HudCpuHeatmapPanel;
    internal Border HudGpuBarFill => SystemHudContainer.HudGpuBarFill;
    internal TextBlock HudGpuHeroText => SystemHudContainer.HudGpuHeroText;
    internal TextBlock HudGpuStatusText => SystemHudContainer.HudGpuStatusText;
    internal Border HudRunawayBanner => SystemHudContainer.HudRunawayBanner;
    internal TextBlock HudRunawayText => SystemHudContainer.HudRunawayText;
    internal TextBlock HudHardwareSubtitle => SystemHudContainer.HudHardwareSubtitle;
    internal Button HudHog1KillBtn => SystemHudContainer.HudHog1KillBtn;
    internal TextBlock HudHog1KillText => SystemHudContainer.HudHog1KillText;
    internal TextBlock HudHog1Name => SystemHudContainer.HudHog1Name;
    internal TextBlock HudHog1Stats => SystemHudContainer.HudHog1Stats;
    internal Button HudHog2KillBtn => SystemHudContainer.HudHog2KillBtn;
    internal TextBlock HudHog2KillText => SystemHudContainer.HudHog2KillText;
    internal TextBlock HudHog2Name => SystemHudContainer.HudHog2Name;
    internal TextBlock HudHog2Stats => SystemHudContainer.HudHog2Stats;
    internal Button HudHog3KillBtn => SystemHudContainer.HudHog3KillBtn;
    internal TextBlock HudHog3KillText => SystemHudContainer.HudHog3KillText;
    internal TextBlock HudHog3Name => SystemHudContainer.HudHog3Name;
    internal TextBlock HudHog3Stats => SystemHudContainer.HudHog3Stats;
    internal Grid HudHogRow1 => SystemHudContainer.HudHogRow1;
    internal Grid HudHogRow2 => SystemHudContainer.HudHogRow2;
    internal Grid HudHogRow3 => SystemHudContainer.HudHogRow3;
    internal Button HudPurgeRamButton => SystemHudContainer.HudPurgeRamButton;
    internal TextBlock HudPurgeRamText => SystemHudContainer.HudPurgeRamText;
    internal Button HudPurgeRunawaysButton => SystemHudContainer.HudPurgeRunawaysButton;
    internal TextBlock HudPurgeRunawaysText => SystemHudContainer.HudPurgeRunawaysText;
    internal Border HudRamBarFill => SystemHudContainer.HudRamBarFill;
    internal TextBlock HudRamHeroText => SystemHudContainer.HudRamHeroText;
    internal TextBlock HudRamStatusText => SystemHudContainer.HudRamStatusText;
    internal TextBlock HudSystemHealthChip => SystemHudContainer.HudSystemHealthChip;

}
