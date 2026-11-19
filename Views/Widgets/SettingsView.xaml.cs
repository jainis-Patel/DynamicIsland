using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class SettingsView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public SettingsView()
    {
        InitializeComponent();
    }

    private void AnimationSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.AnimationSpeedSlider_ValueChanged(sender, e);

    private void AutoPowerAcToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AutoPowerAcToggleBtn_Click(sender, e);

    private void AutoPowerSaverToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AutoPowerSaverToggleBtn_Click(sender, e);

    private void BackdropOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.BackdropOpacitySlider_ValueChanged(sender, e);

    private void BannerModeBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.BannerModeBtn_Click(sender, e);

    private void CameraLensToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.CameraLensToggleBtn_Click(sender, e);

    private void ChargingPulseToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ChargingPulseToggleBtn_Click(sender, e);

    private void ClockStyleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ClockStyleBtn_Click(sender, e);

    private void ClockScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        if (ClockStylesScrollViewer != null)
            Island?.SmoothScrollHorizontal(ClockStylesScrollViewer, -240);
    }

    private void ClockScrollRight_Click(object sender, RoutedEventArgs e)
    {
        if (ClockStylesScrollViewer != null)
            Island?.SmoothScrollHorizontal(ClockStylesScrollViewer, 240);
    }

    private void DndToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DndToggleBtn_Click(sender, e);

    private void OpenNotifCenterBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.OpenNotifCenterBtn_Click(sender, e);

    private void ReplaceBannersToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ReplaceBannersToggleBtn_Click(sender, e);

    private void EdgeBorderToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.EdgeBorderToggleBtn_Click(sender, e);

    private void DockButton_Click(object sender, RoutedEventArgs e) =>
        Island?.DockButton_Click(sender, e);

    private void ExpandTriggerBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ExpandTriggerBtn_Click(sender, e);

    private void GhostModifierBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.GhostModifierBtn_Click(sender, e);

    private void QuickTuckToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.QuickTuckToggleBtn_Click(sender, e);

    private void DockPinToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DockPinToggleBtn_Click(sender, e);

    private void DockPinAllBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DockPinAllBtn_Click(sender, e);

    private void DockPinMinimalBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DockPinMinimalBtn_Click(sender, e);

    private void DockPinResetBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DockPinResetBtn_Click(sender, e);

    private void SettingsTrimRamBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.SettingsTrimRamBtn_Click(sender, e);

    private void SettingsOpenDataBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.SettingsOpenDataBtn_Click(sender, e);

    private void ExportSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ExportSettingsBtn_Click(sender, e);

    private void FollowMonitorToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.FollowMonitorToggleBtn_Click(sender, e);

    private void HaloToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HaloToggleBtn_Click(sender, e);

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.HorizontalScrollViewer_PreviewMouseWheel(sender, e);

    private void HotkeyRecordBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HotkeyRecordBtn_Click(sender, e);

    private void ImportSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ImportSettingsBtn_Click(sender, e);

    private void IslandScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.IslandScaleSlider_ValueChanged(sender, e);

    private void GlassBlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.GlassBlurSlider_ValueChanged(sender, e);

    private void GlassRefractionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.GlassRefractionSlider_ValueChanged(sender, e);

    private void LiquidIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.LiquidIntensitySlider_ValueChanged(sender, e);

    private void LiquidPulseBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.LiquidPulseBtn_Click(sender, e);

    private void LowBattThresholdBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.LowBattThresholdBtn_Click(sender, e);

    private void LyricsToggleBtnSettings_Click(object sender, RoutedEventArgs e) =>
        Island?.LyricsToggleBtnSettings_Click(sender, e);

    private void CompactLyricsToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.CompactLyricsToggleBtn_Click(sender, e);

    private void MonitorSwitchBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.MonitorSwitchBtn_Click(sender, e);

    private void MotionModeBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.MotionModeBtn_Click(sender, e);

    private void NetSpeedIdleToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NetSpeedIdleToggleBtn_Click(sender, e);

    private void NightLightSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NightLightSettingsBtn_Click(sender, e);

    private void NightLightWarmthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NightLightWarmthSlider_ValueChanged(sender, e);

    private void NotchBezelToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NotchBezelToggleBtn_Click(sender, e);

    private void FloatingPiPToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.FloatingPiPToggleBtn_Click(sender, e);

    private void NotchOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NotchOffsetSlider_ValueChanged(sender, e);

    private void PetCompanion_Click(object sender, MouseButtonEventArgs e) =>
        Island?.PetCompanion_Click(sender, e);

    private void OpenPetPlaygroundBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.OpenPetPlaygroundBtn_Click(sender, e);

    private void RoamDesktopBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.RoamDesktopBtn_Click(sender, e);

    private void PetHydrateBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetHydrateBtn_Click(sender, e);

    private void PetInteractBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetInteractBtn_Click(sender, e);

    private void PetReminderCycleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetReminderCycleBtn_Click(sender, e);

    private void PetSpeciesBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetSpeciesBtn_Click(sender, e);

    private void PetAccessoryBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetAccessoryBtn_Click(sender, e);

    private void PetNameBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetNameBtn_Click(sender, e);

    private void PetTreatBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetTreatBtn_Click(sender, e);

    private void PetTrickBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetTrickBtn_Click(sender, e);

    private void PetStretchBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetStretchBtn_Click(sender, e);

    private void PetToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.PetToggleBtn_Click(sender, e);

    private void ResetSettingsBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ResetSettingsBtn_Click(sender, e);

    private void SecondaryDropletToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.SecondaryDropletToggleBtn_Click(sender, e);

    private void SettingsTabBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.SettingsTabBtn_Click(sender, e);

    private void DropletToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletToggleBtn_Click(sender, e);

    private void DropletActionBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletActionBtn_Click(sender, e);

    private void DropletFilterBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletFilterBtn_Click(sender, e);

    private void DropletSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Island?.DropletSearchBox_TextChanged(sender, e);

    private void DropletEnableAllBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletEnableAllBtn_Click(sender, e);

    private void DropletDisableAllBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletDisableAllBtn_Click(sender, e);

    private void DropletResetBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DropletResetBtn_Click(sender, e);

    private void StartupToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.StartupToggleBtn_Click(sender, e);

    private void ThemeModeBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ThemeModeBtn_Click(sender, e);

    private void ThemeStyleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ThemeStyleBtn_Click(sender, e);

    private void IslandThemeBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.IslandThemeBtn_Click(sender, e);

    private void ThemeSwatch_Click(object sender, RoutedEventArgs e) =>
        Island?.ThemeSwatch_Click(sender, e);

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void VisualizerStyleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.VisualizerStyleBtn_Click(sender, e);

    private void VolumeOsdBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.VolumeOsdBtn_Click(sender, e);

    private void NatureDensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NatureDensitySlider_ValueChanged(sender, e);

    private void NatureBloomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NatureBloomSlider_ValueChanged(sender, e);

    private void NatureScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NatureScaleSlider_ValueChanged(sender, e);

    private void NatureDropSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NatureDropSlider_ValueChanged(sender, e);

    private void NatureButterflyBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NatureButterflyBtn_Click(sender, e);

    private void NaturePollenSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Island?.NaturePollenSlider_ValueChanged(sender, e);

    private void NaturePollenToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NaturePollenToggleBtn_Click(sender, e);

    private void NaturePresetBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.NaturePresetBtn_Click(sender, e);

    private void GoToThemesTabBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.GoToThemesTabBtn_Click(sender, e);

    private void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.CheckUpdatesBtn_Click(sender, e);

    private void UpdateChannelBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.UpdateChannelBtn_Click(sender, e);

    private void InstallUpdateBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.InstallUpdateBtn_Click(sender, e);

    private void ViewChangelogBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ViewChangelogBtn_Click(sender, e);

    private void AutoCheckToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AutoCheckToggleBtn_Click(sender, e);

    private void AutoDownloadToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.AutoDownloadToggleBtn_Click(sender, e);

    private void ToggleVersionHistoryBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.ToggleVersionHistoryBtn_Click(sender, e);

    private void GitHubTokenBox_LostFocus(object sender, RoutedEventArgs e) =>
        Island?.GitHubTokenBox_LostFocus(sender, e);
}
