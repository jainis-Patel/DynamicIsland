using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class MechSoundsView : UserControl
{
    private IslandWindow? _window;
    private int _keyPressCount;

    public MechSoundsView()
    {
        InitializeComponent();
        Loaded += MechSoundsView_Loaded;
    }

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        (_window ?? IslandWindow.Instance)?.NavigateToView(Core.ExpandedView.Launcher);

    private void MechSoundsView_Loaded(object sender, RoutedEventArgs e)
    {
        _window = IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;
        RefreshUI();
    }

    public void RefreshUI()
    {
        var service = _window?.KeyboardSound;
        if (service == null) return;

        // Master active state
        bool enabled = service.IsEnabled;
        MasterToggleText.Text = enabled ? "Active" : "Muted";
        MasterToggleGlyph.Text = enabled ? "\uE767" : "\uE74F";
        MasterToggleGlyph.Foreground = enabled ? ThemeBrushes.Green : ThemeBrushes.MutedGrey;

        // Profiles borders
        var activeProfile = service.Profile;
        CherryCard.BorderBrush = activeProfile == MechSoundProfile.CherryBlue ? ThemeBrushes.Accent : ThemeBrushes.CardBorder;
        BrownCard.BorderBrush = activeProfile == MechSoundProfile.TactileBrown ? ThemeBrushes.Amber : ThemeBrushes.CardBorder;
        TypewriterCard.BorderBrush = activeProfile == MechSoundProfile.RetroTypewriter ? ThemeBrushes.Green : ThemeBrushes.CardBorder;

        // Volume
        int volPct = (int)Math.Round(service.Volume * 100);
        VolumeSlider.Value = volPct;
        VolumeValueText.Text = $"{volPct}%";
    }

    private void SelectCherry_Click(object sender, MouseButtonEventArgs e)
    {
        var service = _window?.KeyboardSound;
        if (service == null) return;

        service.Profile = MechSoundProfile.CherryBlue;
        if (!service.IsEnabled) service.SetEnabled(true);
        service.PlayKeyClick();
        _window?.TriggerLiquidImpulse(24, -8, 3);
        RefreshUI();
    }

    private void SelectBrown_Click(object sender, MouseButtonEventArgs e)
    {
        var service = _window?.KeyboardSound;
        if (service == null) return;

        service.Profile = MechSoundProfile.TactileBrown;
        if (!service.IsEnabled) service.SetEnabled(true);
        service.PlayKeyClick();
        _window?.TriggerLiquidImpulse(24, -8, 3);
        RefreshUI();
    }

    private void SelectTypewriter_Click(object sender, MouseButtonEventArgs e)
    {
        var service = _window?.KeyboardSound;
        if (service == null) return;

        service.Profile = MechSoundProfile.RetroTypewriter;
        if (!service.IsEnabled) service.SetEnabled(true);
        service.PlayKeyClick();
        _window?.TriggerLiquidImpulse(24, -8, 3);
        RefreshUI();
    }

    private void ToggleSoundActive_Click(object sender, RoutedEventArgs e)
    {
        var service = _window?.KeyboardSound;
        if (service == null) return;

        bool next = !service.IsEnabled;
        service.SetEnabled(next);
        if (_window?.Droplets != null)
        {
            _window.Droplets.SetState("mech_sounds", next);
        }

        if (next) service.PlayKeyClick();
        _window?.TriggerLiquidImpulse(26, -9, 3);
        RefreshUI();
    }

    private void TestClickBtn_Click(object sender, RoutedEventArgs e)
    {
        _window?.KeyboardSound?.PlayKeyClick();
        _window?.TriggerLiquidImpulse(18, -6, 2);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var service = _window?.KeyboardSound;
        if (service == null) return;

        int volPct = (int)Math.Round(e.NewValue);
        service.Volume = (float)(volPct / 100.0);
        if (VolumeValueText != null) VolumeValueText.Text = $"{volPct}%";
    }

    private void TestTypingBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _window?.KeyboardSound?.PlayKeyClick();
        _keyPressCount++;
        KeyCounterText.Text = $"Key: {e.Key} (#{_keyPressCount})";
        KeyCounterText.Foreground = ThemeBrushes.Accent;
    }
}
