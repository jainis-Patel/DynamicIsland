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
    internal PlayerDiscControl ExpandedArtDisc => MediaContainer.ExpandedArtDisc;
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
    internal Button ClearShelfButton => EnsureShelfContainer().ClearShelfButton;
    internal System.Windows.Controls.Button CopyAllShelfButton => EnsureShelfContainer().CopyAllShelfButton;
    internal System.Windows.Controls.Border DragAllShelfBtn => EnsureShelfContainer().DragAllShelfBtn;
    internal System.Windows.Controls.Border ShelfCountBadge => EnsureShelfContainer().ShelfCountBadge;
    internal TextBlock ShelfCountText => EnsureShelfContainer().ShelfCountText;
    internal FrameworkElement ShelfEmptyPlaceholder => EnsureShelfContainer().ShelfEmptyPlaceholder;
    internal ItemsControl ShelfItemsControl => EnsureShelfContainer().ShelfItemsControl;
    internal System.Windows.Controls.Button ShelfScrollLeftBtn => EnsureShelfContainer().ShelfScrollLeftBtn;
    internal System.Windows.Controls.Button ShelfScrollRightBtn => EnsureShelfContainer().ShelfScrollRightBtn;
    internal System.Windows.Controls.ScrollViewer ShelfScrollViewer => EnsureShelfContainer().ShelfScrollViewer;
    internal Button ZipShelfButton => EnsureShelfContainer().ZipShelfButton;
    internal Button WindowsShareButton => EnsureShelfContainer().WindowsShareButton;
    internal Button PhoneShareButton => EnsureShelfContainer().PhoneShareButton;
    internal Button ShelfActionsButton => EnsureShelfContainer().ShelfActionsButton;
    internal Button SummonBasketButton => EnsureShelfContainer().SummonBasketButton;
    internal FrameworkElement? ShelfItemActionsGroup => EnsureShelfContainer().ShelfItemActionsGroup;

    // -------------------------------------------------------------------------
    //  Clipboard View Forwarders (ClipboardContainer)
    // -------------------------------------------------------------------------
    internal Button ClearClipboardButton => EnsureClipboardContainer().ClearClipboardButton;
    internal Button CaptureRegionButton => EnsureClipboardContainer().CaptureRegionButton;
    internal Button ClipboardQuickPickerButton => EnsureClipboardContainer().ClipboardQuickPickerButton;
    internal TextBlock ClipboardQueueBtnText => EnsureClipboardContainer().ClipboardQueueBtnText;
    internal System.Windows.Controls.Button ClearClipboardSearchBtn => EnsureClipboardContainer().ClearClipboardSearchBtn;
    internal TextBlock ClipboardCountText => EnsureClipboardContainer().ClipboardCountText;
    internal FrameworkElement ClipboardEmptyPlaceholder => EnsureClipboardContainer().ClipboardEmptyPlaceholder;
    internal ItemsControl ClipboardItemsControl => EnsureClipboardContainer().ClipboardItemsControl;
    internal System.Windows.Controls.Button ClipboardScrollLeftBtn => EnsureClipboardContainer().ClipboardScrollLeftBtn;
    internal System.Windows.Controls.Button ClipboardScrollRightBtn => EnsureClipboardContainer().ClipboardScrollRightBtn;
    internal System.Windows.Controls.ScrollViewer ClipboardScrollViewer => EnsureClipboardContainer().ClipboardScrollViewer;
    internal System.Windows.Controls.TextBox ClipboardSearchBox => EnsureClipboardContainer().ClipboardSearchBox;
    internal System.Windows.Controls.Button ClipFilterAllBtn => EnsureClipboardContainer().ClipFilterAllBtn;
    internal System.Windows.Controls.Button ClipFilterCodeBtn => EnsureClipboardContainer().ClipFilterCodeBtn;
    internal System.Windows.Controls.Button ClipFilterColorsBtn => EnsureClipboardContainer().ClipFilterColorsBtn;
    internal System.Windows.Controls.Button ClipFilterImagesBtn => EnsureClipboardContainer().ClipFilterImagesBtn;
    internal System.Windows.Controls.Button ClipFilterLinksBtn => EnsureClipboardContainer().ClipFilterLinksBtn;
    internal System.Windows.Controls.Button ClipFilterPinnedBtn => EnsureClipboardContainer().ClipFilterPinnedBtn;
    internal Button EyedropperButton => EnsureClipboardContainer().EyedropperButton;
    internal Button OcrClipboardButton => EnsureClipboardContainer().OcrClipboardButton;

    // -------------------------------------------------------------------------
    //  Calc View Forwarders (CalcContainer)
    // -------------------------------------------------------------------------
    internal TextBlock CalcExplanationText => EnsureCalcContainer().CalcExplanationText;
    internal ItemsControl CalcHistoryItemsControl => EnsureCalcContainer().CalcHistoryItemsControl;
    internal TextBox CalcInputBox => EnsureCalcContainer().CalcInputBox;
    internal TextBlock CalcPlaceholder => EnsureCalcContainer().CalcPlaceholder;
    internal Border CalcResultHost => EnsureCalcContainer().CalcResultHost;
    internal TextBlock CalcResultText => EnsureCalcContainer().CalcResultText;

    // -------------------------------------------------------------------------
    //  Notes View Forwarders (NotesContainer)
    // -------------------------------------------------------------------------
    internal TextBox NoteEditorBox => EnsureNotesContainer().NoteEditorBox;
    internal ItemsControl NotesListControl => EnsureNotesContainer().NotesListControl;

    // -------------------------------------------------------------------------
    //  Settings View Forwarders (Minimal cross-partial accessor)
    // -------------------------------------------------------------------------
    internal DynamicIsland.Core.PetHostControl? SettingsHeroPet => _settingsContainer?.PetPanel.SettingsHeroPet;

    // -------------------------------------------------------------------------
    //  Audio View Forwarders (AudioContainer)
    // -------------------------------------------------------------------------
    internal Border AudioAppMixerEmptyPlaceholder => EnsureAudioContainer().AudioAppMixerEmptyPlaceholder;
    internal ScrollViewer AudioAppMixerScrollViewer => EnsureAudioContainer().AudioAppMixerScrollViewer;
    internal ItemsControl AudioAppSessionsControl => EnsureAudioContainer().AudioAppSessionsControl;
    internal ItemsControl AudioEndpointsControl => EnsureAudioContainer().AudioEndpointsControl;
    internal ScrollViewer AudioEndpointsScrollViewer => EnsureAudioContainer().AudioEndpointsScrollViewer;
    internal TextBlock AudioSectionTitle => EnsureAudioContainer().AudioSectionTitle;
    internal Button AudioModeOutputsBtn => EnsureAudioContainer().AudioModeOutputsBtn;
    internal Button AudioModeMixerBtn => EnsureAudioContainer().AudioModeMixerBtn;
    internal Button HardwareMicBtn => EnsureAudioContainer().HardwareMicBtn;
    internal TextBlock HardwareMicGlyph => EnsureAudioContainer().HardwareMicGlyph;
    internal TextBlock HardwareMicText => EnsureAudioContainer().HardwareMicText;
    internal TextBlock HeroDeviceGlyph => EnsureAudioContainer().HeroDeviceGlyph;
    internal TextBlock HeroDeviceNameText => EnsureAudioContainer().HeroDeviceNameText;
    internal TextBlock HeroDeviceTypeText => EnsureAudioContainer().HeroDeviceTypeText;
    internal Border HeroDeviceBatteryBadge => EnsureAudioContainer().HeroDeviceBatteryBadge;
    internal TextBlock HeroDeviceBatteryText => EnsureAudioContainer().HeroDeviceBatteryText;
    internal Button HeroMasterMuteBtn => EnsureAudioContainer().HeroMasterMuteBtn;
    internal TextBlock HeroMasterMuteGlyph => EnsureAudioContainer().HeroMasterMuteGlyph;
    internal TextBlock HeroSpeakerIcon => EnsureAudioContainer().HeroSpeakerIcon;
    internal Slider HeroMasterVolumeSlider => EnsureAudioContainer().HeroMasterVolumeSlider;
    internal TextBlock HeroMasterVolumePercentText => EnsureAudioContainer().HeroMasterVolumePercentText;

    // -------------------------------------------------------------------------
    //  Battery View Forwarders (BatteryContainer)
    // -------------------------------------------------------------------------
    internal TextBlock BatteryChargingBolt => EnsureBatteryContainer().BatteryChargingBolt;
    internal Border BatteryHeaderBadge => EnsureBatteryContainer().BatteryHeaderBadge;
    internal TextBlock BatteryHeaderBadgeText => EnsureBatteryContainer().BatteryHeaderBadgeText;
    internal System.Windows.Shapes.Ellipse BatteryHeaderDot => EnsureBatteryContainer().BatteryHeaderDot;
    internal System.Windows.Controls.TextBlock BatteryHealthText => EnsureBatteryContainer().BatteryHealthText;
    internal TextBlock BatteryHeroPercentText => EnsureBatteryContainer().BatteryHeroPercentText;
    internal Border BatteryLevelFill => EnsureBatteryContainer().BatteryLevelFill;
    internal TextBlock BatteryStatusSubtitle => EnsureBatteryContainer().BatteryStatusSubtitle;
    internal TextBlock BatteryTimeEstimate => EnsureBatteryContainer().BatteryTimeEstimate;
    internal System.Windows.Controls.Border BatteryWattageBadge => EnsureBatteryContainer().BatteryWattageBadge;
    internal System.Windows.Controls.TextBlock BatteryWattageText => EnsureBatteryContainer().BatteryWattageText;
    internal Slider BrightnessSlider => EnsureBatteryContainer().BrightnessSlider;
    internal TextBlock BrightnessValueText => EnsureBatteryContainer().BrightnessValueText;
    internal Button CaffeineQuickBtn => EnsureBatteryContainer().CaffeineQuickBtn;
    internal TextBlock CaffeineQuickIcon => EnsureBatteryContainer().CaffeineQuickIcon;
    internal TextBlock CaffeineQuickText => EnsureBatteryContainer().CaffeineQuickText;
    internal System.Windows.Controls.Border DisplayRefreshBadge => EnsureBatteryContainer().DisplayRefreshBadge;
    internal System.Windows.Controls.TextBlock DisplayRefreshText => EnsureBatteryContainer().DisplayRefreshText;
    internal System.Windows.Controls.TextBlock DisplayResolutionText => EnsureBatteryContainer().DisplayResolutionText;
    internal System.Windows.Controls.Button NightLightQuickBtn => EnsureBatteryContainer().NightLightQuickBtn;
    internal System.Windows.Controls.TextBlock NightLightQuickIcon => EnsureBatteryContainer().NightLightQuickIcon;
    internal System.Windows.Controls.TextBlock NightLightQuickText => EnsureBatteryContainer().NightLightQuickText;
    internal Border PlanBalancedCard => EnsureBatteryContainer().PlanBalancedCard;
    internal System.Windows.Controls.TextBlock PlanBalancedIcon => EnsureBatteryContainer().PlanBalancedIcon;
    internal System.Windows.Controls.TextBlock PlanBalancedSub => EnsureBatteryContainer().PlanBalancedSub;
    internal System.Windows.Controls.TextBlock PlanBalancedTitle => EnsureBatteryContainer().PlanBalancedTitle;
    internal Border PlanPerfCard => EnsureBatteryContainer().PlanPerfCard;
    internal System.Windows.Controls.TextBlock PlanPerfIcon => EnsureBatteryContainer().PlanPerfIcon;
    internal System.Windows.Controls.TextBlock PlanPerfSub => EnsureBatteryContainer().PlanPerfSub;
    internal System.Windows.Controls.TextBlock PlanPerfTitle => EnsureBatteryContainer().PlanPerfTitle;
    internal Border PlanSaverCard => EnsureBatteryContainer().PlanSaverCard;
    internal System.Windows.Controls.TextBlock PlanSaverIcon => EnsureBatteryContainer().PlanSaverIcon;
    internal System.Windows.Controls.TextBlock PlanSaverSub => EnsureBatteryContainer().PlanSaverSub;
    internal System.Windows.Controls.TextBlock PlanSaverTitle => EnsureBatteryContainer().PlanSaverTitle;

    // -------------------------------------------------------------------------
    //  VoiceMemo View Forwarders (VoiceMemoContainer)
    // -------------------------------------------------------------------------
    internal Button HeroRecordBtn => EnsureVoiceMemoContainer().HeroRecordBtn;
    internal TextBlock HeroRecordGlyph => EnsureVoiceMemoContainer().HeroRecordGlyph;
    internal TextBlock HeroRecordStatus => EnsureVoiceMemoContainer().HeroRecordStatus;
    internal TextBlock HeroRecordTimer => EnsureVoiceMemoContainer().HeroRecordTimer;
    internal TextBlock LastMemoDuration => EnsureVoiceMemoContainer().LastMemoDuration;
    internal TextBlock LastMemoFileName => EnsureVoiceMemoContainer().LastMemoFileName;
    internal StackPanel LastMemoHost => EnsureVoiceMemoContainer().LastMemoHost;
    internal TextBlock MemoPlayGlyph => EnsureVoiceMemoContainer().MemoPlayGlyph;
    internal TextBlock MemoPlayLabel => EnsureVoiceMemoContainer().MemoPlayLabel;
    internal Button MemoPlayPauseBtn => EnsureVoiceMemoContainer().MemoPlayPauseBtn;
    internal StackPanel NoMemoPlaceholder => EnsureVoiceMemoContainer().NoMemoPlaceholder;
    internal System.Windows.Shapes.Rectangle RecBar0 => EnsureVoiceMemoContainer().RecBar0;
    internal System.Windows.Shapes.Rectangle RecBar1 => EnsureVoiceMemoContainer().RecBar1;
    internal System.Windows.Shapes.Rectangle RecBar2 => EnsureVoiceMemoContainer().RecBar2;
    internal System.Windows.Shapes.Rectangle RecBar3 => EnsureVoiceMemoContainer().RecBar3;
    internal System.Windows.Shapes.Rectangle RecBar4 => EnsureVoiceMemoContainer().RecBar4;
    internal Border RecordHalo => EnsureVoiceMemoContainer().RecordHalo;
    internal ItemsControl VoiceMemosItemsControl => EnsureVoiceMemoContainer().VoiceMemosItemsControl;
    internal ScrollViewer VoiceMemosScrollViewer => EnsureVoiceMemoContainer().VoiceMemosScrollViewer;

    // -------------------------------------------------------------------------
    //  Peripherals View Forwarders (PeripheralsContainer)
    // -------------------------------------------------------------------------
    internal Button OpenBluetoothSettingsBtn => EnsurePeripheralsContainer().OpenBluetoothSettingsBtn;
    internal StackPanel PeripheralsEmptyPlaceholder => EnsurePeripheralsContainer().PeripheralsEmptyPlaceholder;
    internal Button PeripheralsFilterBtn => EnsurePeripheralsContainer().PeripheralsFilterBtn;
    internal TextBlock PeripheralsFilterText => EnsurePeripheralsContainer().PeripheralsFilterText;
    internal ItemsControl PeripheralsItemsControl => EnsurePeripheralsContainer().PeripheralsItemsControl;
    internal System.Windows.Controls.ScrollViewer PeripheralsScrollViewer => EnsurePeripheralsContainer().PeripheralsScrollViewer;
    internal Button RefreshPeripheralsBtn => EnsurePeripheralsContainer().RefreshPeripheralsBtn;

    // -------------------------------------------------------------------------
    //  Network View Forwarders (NetworkContainer)
    // -------------------------------------------------------------------------
    internal Border BtToggleCard => EnsureNetworkContainer().BtToggleCard;
    internal TextBlock BtToggleGlyph => EnsureNetworkContainer().BtToggleGlyph;
    internal TextBlock BtToggleStateText => EnsureNetworkContainer().BtToggleStateText;
    internal Border HotspotCard => EnsureNetworkContainer().HotspotCard;
    internal TextBlock NetworkDownSpeedText => EnsureNetworkContainer().NetworkDownSpeedText;
    internal TextBlock NetworkHeroGlyph => EnsureNetworkContainer().NetworkHeroGlyph;
    internal Border NetworkHeroIconBg => EnsureNetworkContainer().NetworkHeroIconBg;
    internal TextBlock NetworkHeroName => EnsureNetworkContainer().NetworkHeroName;
    internal TextBlock NetworkHeroSubtitle => EnsureNetworkContainer().NetworkHeroSubtitle;
    internal Border NetworkIpBadge => EnsureNetworkContainer().NetworkIpBadge;
    internal TextBlock NetworkIpText => EnsureNetworkContainer().NetworkIpText;
    internal Grid NetworkOverviewHost => EnsureNetworkContainer().NetworkOverviewHost;
    internal System.Windows.Controls.Button NetworkPillSpeedToggleBtn => EnsureNetworkContainer().NetworkPillSpeedToggleBtn;
    internal System.Windows.Controls.TextBlock NetworkPillSpeedToggleText => EnsureNetworkContainer().NetworkPillSpeedToggleText;
    internal System.Windows.Shapes.Ellipse NetworkPingDot => EnsureNetworkContainer().NetworkPingDot;
    internal System.Windows.Controls.TextBlock NetworkPingText => EnsureNetworkContainer().NetworkPingText;
    internal TextBlock NetworkSectionTitle => EnsureNetworkContainer().NetworkSectionTitle;
    internal TextBlock NetworkUpSpeedText => EnsureNetworkContainer().NetworkUpSpeedText;
    internal Button OpenNetworkSettingsBtn => EnsureNetworkContainer().OpenNetworkSettingsBtn;
    internal Button ScanWifiModeBtn => EnsureNetworkContainer().ScanWifiModeBtn;
    internal TextBlock ScanWifiModeText => EnsureNetworkContainer().ScanWifiModeText;
    internal Border WifiNetworksCard => EnsureNetworkContainer().WifiNetworksCard;
    internal ItemsControl WifiNetworksItemsControl => EnsureNetworkContainer().WifiNetworksItemsControl;
    internal ScrollViewer WifiNetworksScrollViewer => EnsureNetworkContainer().WifiNetworksScrollViewer;
    internal Grid WifiScannerHost => EnsureNetworkContainer().WifiScannerHost;
    internal Border WifiScannerStatusPlaceholder => EnsureNetworkContainer().WifiScannerStatusPlaceholder;
    internal TextBlock WifiScannerStatusSubtitle => EnsureNetworkContainer().WifiScannerStatusSubtitle;
    internal TextBlock WifiScannerStatusTitle => EnsureNetworkContainer().WifiScannerStatusTitle;
    internal Border WifiToggleCard => EnsureNetworkContainer().WifiToggleCard;
    internal TextBlock WifiToggleGlyph => EnsureNetworkContainer().WifiToggleGlyph;
    internal TextBlock WifiToggleStateText => EnsureNetworkContainer().WifiToggleStateText;

    // -------------------------------------------------------------------------
    //  Notifications View Forwarders (NotificationsContainer)
    // -------------------------------------------------------------------------
    internal Button ClearNotificationsButton => EnsureNotificationsContainer().ClearNotificationsButton;
    internal System.Windows.Controls.Border NotificationsCountBadge => EnsureNotificationsContainer().NotificationsCountBadge;
    internal TextBlock NotificationsCountText => EnsureNotificationsContainer().NotificationsCountText;
    internal Button NotificationsDndButton => EnsureNotificationsContainer().NotificationsDndButton;
    internal TextBlock NotificationsDndGlyph => EnsureNotificationsContainer().NotificationsDndGlyph;
    internal TextBlock NotificationsDndText => EnsureNotificationsContainer().NotificationsDndText;
    internal DockPanel NotificationsFilterBar => EnsureNotificationsContainer().NotificationsFilterBar;
    internal Button NotifFilterAllBtn => EnsureNotificationsContainer().NotifFilterAllBtn;
    internal Button NotifFilterMessagesBtn => EnsureNotificationsContainer().NotifFilterMessagesBtn;
    internal TextBox NotificationsSearchBox => EnsureNotificationsContainer().NotificationsSearchBox;
    internal Button ClearNotificationsSearchBtn => EnsureNotificationsContainer().ClearNotificationsSearchBtn;
    internal StackPanel NotificationsEmptyPlaceholder => EnsureNotificationsContainer().NotificationsEmptyPlaceholder;
    internal TextBlock NotificationsEmptyGlyph => EnsureNotificationsContainer().NotificationsEmptyGlyph;
    internal TextBlock NotificationsEmptyTitle => EnsureNotificationsContainer().NotificationsEmptyTitle;
    internal TextBlock NotificationsEmptySubtitle => EnsureNotificationsContainer().NotificationsEmptySubtitle;
    internal Button NotificationsEmptyActionBtn => EnsureNotificationsContainer().NotificationsEmptyActionBtn;
    internal TextBlock NotificationsEmptyActionText => EnsureNotificationsContainer().NotificationsEmptyActionText;
    internal ItemsControl NotificationsItemsControl => EnsureNotificationsContainer().NotificationsItemsControl;
    internal System.Windows.Controls.ScrollViewer NotificationsScrollViewer => EnsureNotificationsContainer().NotificationsScrollViewer;
    internal Button UnmuteAllAppsButton => EnsureNotificationsContainer().UnmuteAllAppsButton;
    internal TextBlock UnmuteAllAppsText => EnsureNotificationsContainer().UnmuteAllAppsText;
    internal Button ToggleGroupingButton => EnsureNotificationsContainer().ToggleGroupingButton;
    internal TextBlock ToggleGroupingGlyph => EnsureNotificationsContainer().ToggleGroupingGlyph;
    internal TextBlock ToggleGroupingText => EnsureNotificationsContainer().ToggleGroupingText;

    // -------------------------------------------------------------------------
    //  SystemHud View Forwarders (SystemHudContainer)
    // -------------------------------------------------------------------------
    internal Border HudCpuBarFill => EnsureSystemHudContainer().HudCpuBarFill;
    internal TextBlock HudCpuHeroText => EnsureSystemHudContainer().HudCpuHeroText;
    internal TextBlock HudCpuStatusText => EnsureSystemHudContainer().HudCpuStatusText;
    internal StackPanel HudCpuHeatmapPanel => EnsureSystemHudContainer().HudCpuHeatmapPanel;
    internal Border HudGpuBarFill => EnsureSystemHudContainer().HudGpuBarFill;
    internal TextBlock HudGpuHeroText => EnsureSystemHudContainer().HudGpuHeroText;
    internal TextBlock HudGpuStatusText => EnsureSystemHudContainer().HudGpuStatusText;
    internal Border HudRunawayBanner => EnsureSystemHudContainer().HudRunawayBanner;
    internal TextBlock HudRunawayText => EnsureSystemHudContainer().HudRunawayText;
    internal TextBlock HudHardwareSubtitle => EnsureSystemHudContainer().HudHardwareSubtitle;
    internal Button HudHog1KillBtn => EnsureSystemHudContainer().HudHog1KillBtn;
    internal TextBlock HudHog1KillText => EnsureSystemHudContainer().HudHog1KillText;
    internal TextBlock HudHog1Name => EnsureSystemHudContainer().HudHog1Name;
    internal TextBlock HudHog1Stats => EnsureSystemHudContainer().HudHog1Stats;
    internal Button HudHog2KillBtn => EnsureSystemHudContainer().HudHog2KillBtn;
    internal TextBlock HudHog2KillText => EnsureSystemHudContainer().HudHog2KillText;
    internal TextBlock HudHog2Name => EnsureSystemHudContainer().HudHog2Name;
    internal TextBlock HudHog2Stats => EnsureSystemHudContainer().HudHog2Stats;
    internal Button HudHog3KillBtn => EnsureSystemHudContainer().HudHog3KillBtn;
    internal TextBlock HudHog3KillText => EnsureSystemHudContainer().HudHog3KillText;
    internal TextBlock HudHog3Name => EnsureSystemHudContainer().HudHog3Name;
    internal TextBlock HudHog3Stats => EnsureSystemHudContainer().HudHog3Stats;
    internal Grid HudHogRow1 => EnsureSystemHudContainer().HudHogRow1;
    internal Grid HudHogRow2 => EnsureSystemHudContainer().HudHogRow2;
    internal Grid HudHogRow3 => EnsureSystemHudContainer().HudHogRow3;
    internal Button HudPurgeRamButton => EnsureSystemHudContainer().HudPurgeRamButton;
    internal TextBlock HudPurgeRamText => EnsureSystemHudContainer().HudPurgeRamText;
    internal Button HudPurgeRunawaysButton => EnsureSystemHudContainer().HudPurgeRunawaysButton;
    internal TextBlock HudPurgeRunawaysText => EnsureSystemHudContainer().HudPurgeRunawaysText;
    internal Border HudRamBarFill => EnsureSystemHudContainer().HudRamBarFill;
    internal TextBlock HudRamHeroText => EnsureSystemHudContainer().HudRamHeroText;
    internal TextBlock HudRamStatusText => EnsureSystemHudContainer().HudRamStatusText;
    internal TextBlock HudSystemHealthChip => EnsureSystemHudContainer().HudSystemHealthChip;
    internal Button HudSnoozeResetBtn => EnsureSystemHudContainer().HudSnoozeResetBtn;
    internal TextBlock HudSnoozeResetText => EnsureSystemHudContainer().HudSnoozeResetText;

}
