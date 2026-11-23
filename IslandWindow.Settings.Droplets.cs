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
    internal void DockPinToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string viewKey })
        {
            _settingsService.Update(s =>
            {
                if (!s.HiddenDockViews.Remove(viewKey))
                {
                    s.HiddenDockViews.Add(viewKey);
                }
            }, nameof(AppSettings.HiddenDockViews));
        }
    }

    internal void DockPinAllBtn_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Update(s => s.HiddenDockViews.Clear(), nameof(AppSettings.HiddenDockViews));
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE718",
            "Dock Strip Updated",
            "All 19 tools enabled in dock strip",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "dock_pins"));
    }

    internal void DockPinMinimalBtn_Click(object sender, RoutedEventArgs e)
    {
        var toHide = new List<string>
        {
            "notes", "converter", "launcher", "termi",
            "phonebridge", "mechsounds", "capturestudio",
            "systemhud", "voicememo", "peripherals", "network", "micmute"
        };
        _settingsService.Update(s =>
        {
            s.HiddenDockViews.Clear();
            s.HiddenDockViews.AddRange(toHide);
        }, nameof(AppSettings.HiddenDockViews));
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE718",
            "Minimalist Dock Active",
            "Pinned core essentials: Shelf, Clipboard, Calc, Audio, Settings",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "dock_pins"));
    }

    internal void DockPinResetBtn_Click(object sender, RoutedEventArgs e)
    {
        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (!_confirmGuard.TryConfirm("reset_dock_pins", sender, "Confirm?", bypass: shiftHeld, dispatcher: Dispatcher))
        {
            TriggerLiquidImpulse(18, -6, 2);
            return;
        }

        _settingsService.Update(s => s.HiddenDockViews.Clear(), nameof(AppSettings.HiddenDockViews));
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE777",
            "Dock Reset",
            "Dock configuration restored to defaults",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "dock_pins"));
    }

    private void UpdateDockVisibilityUI()
    {
        var hidden = _settingsService.Current.HiddenDockViews;
        bool IsDisabledByDroplet(string viewKey)
        {
            if (_dropletRegistry == null) return false;
            foreach (var d in _dropletRegistry.Items)
            {
                if (string.Equals(d.DockViewKey, viewKey, StringComparison.OrdinalIgnoreCase) && !d.IsEnabled)
                    return true;
            }
            return false;
        }

        int visibleCount = 0;
        int totalTools = 19;

        var dp = SettingsContainer?.DockPanel;

        void ApplyPin(string key, Button? dockBtn, Button? pinBtn, TextBlock? statusText = null)
        {
            bool isHidden = hidden.Contains(key) || IsDisabledByDroplet(key);
            if (dockBtn != null)
            {
                dockBtn.Visibility = isHidden ? Visibility.Collapsed : Visibility.Visible;
                if (!isHidden) visibleCount++;
            }
            if (pinBtn != null)
            {
                pinBtn.Opacity = isHidden ? 0.5 : 1.0;
                pinBtn.BorderBrush = isHidden ? ThemeBrushes.CardBorder : ThemeBrushes.Green;
                pinBtn.Background = isHidden ? ThemeBrushes.DockInactiveBg : ThemeBrushes.PlanSaverActiveBg;
            }
            if (statusText != null)
            {
                statusText.Text = isHidden ? "Hidden" : "Visible";
                statusText.Foreground = isHidden ? ThemeBrushes.SecondaryText : ThemeBrushes.Green;
            }
        }

        ApplyPin("shelf", ShelfButton, dp?.DockPinShelfBtn, dp?.DockPinShelfText);
        ApplyPin("clipboard", ClipboardButton, dp?.DockPinClipBtn, dp?.DockPinClipText);
        ApplyPin("calc", CalcButton, dp?.DockPinCalcBtn, dp?.DockPinCalcText);
        ApplyPin("notes", NotesButton, dp?.DockPinNotesBtn, dp?.DockPinNotesText);
        ApplyPin("converter", ConverterButton, dp?.DockPinConverterBtn, dp?.DockPinConverterText);
        ApplyPin("widgets", WidgetsGalleryButton, dp?.DockPinWidgetsBtn, dp?.DockPinWidgetsText);
        ApplyPin("launcher", LauncherButton, dp?.DockPinLauncherBtn, dp?.DockPinLauncherText);
        ApplyPin("termi", TermiButton, dp?.DockPinTermiBtn, dp?.DockPinTermiText);
        ApplyPin("phonebridge", PhoneBridgeButton, dp?.DockPinPhoneBridgeBtn, dp?.DockPinPhoneBridgeText);
        ApplyPin("mechsounds", MechSoundsButton, dp?.DockPinMechSoundsBtn, dp?.DockPinMechSoundsText);
        ApplyPin("capturestudio", CaptureStudioButton, dp?.DockPinCaptureStudioBtn, dp?.DockPinCaptureStudioText);
        ApplyPin("audio", AudioButton, dp?.DockPinAudioBtn, dp?.DockPinAudioText);
        ApplyPin("battery", BatteryButton, dp?.DockPinBatteryBtn, dp?.DockPinBatteryText);
        ApplyPin("systemhud", SystemHudButton, dp?.DockPinHudBtn, dp?.DockPinHudText);
        ApplyPin("voicememo", RecordButton, dp?.DockPinMemoBtn, dp?.DockPinMemoText);
        ApplyPin("peripherals", PeripheralsButton, dp?.DockPinDevicesBtn, dp?.DockPinDevicesText);
        ApplyPin("network", NetworkButton, dp?.DockPinWifiBtn, dp?.DockPinWifiText);
        ApplyPin("notifications", NotificationsButton, dp?.DockPinNotifBtn, dp?.DockPinNotifText);
        ApplyPin("micmute", MicMuteButton, dp?.DockPinMicBtn, dp?.DockPinMicText);

        if (dp?.DockHeaderSummaryText != null)
        {
            dp.DockHeaderSummaryText.Text = $"{visibleCount} / {totalTools} Visible in Dock";
        }
    }

    private string _activeDropletCategory = "All";
    private string _dropletSearchFilter = "";

    internal void DropletFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string cat })
        {
            _activeDropletCategory = cat;
            TriggerLiquidImpulse(18, -6, 2);
            UpdateDropletsUI();
        }
    }

    internal void DropletSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _dropletSearchFilter = SettingsContainer?.DropletsPanel?.DropletSearchBox?.Text?.Trim() ?? "";
        UpdateDropletsUI();
    }

    internal void DropletEnableAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dropletRegistry == null) return;
        _dropletRegistry.SetAll(true);
        UpdateDropletsUI();
        UpdateDockVisibilityUI();
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE73E",
            "Modular Droplets",
            $"All {_dropletRegistry.Items.Count} droplets enabled",
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "droplets"));
    }

    internal void DropletDisableAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dropletRegistry == null) return;
        _dropletRegistry.SetAll(false);
        UpdateDropletsUI();
        UpdateDockVisibilityUI();
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE711",
            "Modular Droplets",
            "All droplets disabled (0 KB RAM, 0% CPU)",
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "droplets"));
    }

    internal void DropletResetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dropletRegistry == null) return;

        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (!_confirmGuard.TryConfirm("reset_droplets", sender, "Confirm?", bypass: shiftHeld, dispatcher: Dispatcher))
        {
            TriggerLiquidImpulse(18, -6, 2);
            return;
        }

        _dropletRegistry.ResetToDefaults();
        UpdateDropletsUI();
        UpdateDockVisibilityUI();
        TriggerLiquidImpulse(30, -10, 4);
        _machine.PushToast(new ToastContent(
            "\uE777",
            "Modular Droplets",
            "Reset to factory default droplet configuration",
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "settings",
            Priority: ToastPriority.Feedback,
            Category: "droplets"));
    }

    internal void DropletToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string dropletId } && _dropletRegistry != null)
        {
            bool next = _dropletRegistry.Toggle(dropletId);
            var desc = _dropletRegistry.Get(dropletId);
            UpdateDropletsUI();
            UpdateDockVisibilityUI();
            TriggerLiquidImpulse(24, -8, 3);
            if (_keyboardSoundService != null && _dropletRegistry.IsEnabled("mech_sounds"))
            {
                _keyboardSoundService.PlayKeyClick();
            }
            if (desc != null)
            {
                _machine.PushToast(new ToastContent(
                    desc.Glyph,
                    next ? $"{desc.Title} Enabled" : $"{desc.Title} Disabled",
                    next ? desc.Subtitle : "Hooks & timers unloaded (0 KB overhead, 0% CPU)",
                    next ? ThemeBrushes.GreenColor : ThemeBrushes.MutedGreyColor,
                    TimeSpan.FromSeconds(2.0),
                    TargetView: "settings",
                    Priority: ToastPriority.Feedback,
                    Category: "droplets"));
            }
        }
    }

    internal void DropletActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string dropletId })
        {
            switch (dropletId.ToLowerInvariant())
            {
                case "notes":
                    SwitchExpandedView(ExpandedView.Notes);
                    break;
                case "clipboard_pro":
                    SummonClipboardPopupWindow();
                    break;
                case "element_capture":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.CaptureStudio);
                    CaptureStudioContainer?.UpdatePinnedCountBadge();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "jiggle_basket":
                    SummonBasketButton_Click(sender, e);
                    _machine.PushToast(new ToastContent(
                        "\uE8B7",
                        "Floating Drop Basket",
                        "Summoned basket at cursor · Drop files anywhere",
                        ThemeBrushes.GreenColor,
                        TimeSpan.FromSeconds(2.0),
                        TargetView: "shelf",
                        Priority: ToastPriority.Feedback,
                        Category: "basket"));
                    break;
                case "phone_bridge":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.PhoneBridge);
                    PhoneBridgeContainer?.RefreshSession();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "file_converter":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.Converter);
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "drop_shelf":
                    SwitchExpandedView(ExpandedView.Shelf);
                    break;
                case "quick_calc":
                    SwitchExpandedView(ExpandedView.Calc);
                    break;
                case "voice_memo":
                case "voice_transcription":
                    SwitchExpandedView(ExpandedView.VoiceMemo);
                    break;
                case "system_hud":
                    SwitchExpandedView(ExpandedView.SystemHud);
                    break;
                case "peripherals":
                    SwitchExpandedView(ExpandedView.Peripherals);
                    break;
                case "caffeine":
                    SwitchExpandedView(ExpandedView.Battery);
                    break;
                case "mech_sounds":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.MechSounds);
                    MechSoundsContainer?.RefreshUI();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "quick_launcher":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.Launcher);
                    LauncherContainer?.RefreshItems();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "termi_notch":
                    _machine.ExpandNow();
                    SwitchExpandedView(ExpandedView.Termi);
                    TermiContainer?.PopulateSnippetPills();
                    TriggerLiquidImpulse(32, -10, 4);
                    break;
                case "window_snap":
                    _machine.PushToast(new ToastContent(
                        "\uE7C4",
                        "Window Snapping Active",
                        "Drag any window to top (maximize), edges (halves), or corners (quarters)",
                        ThemeBrushes.BlueColor,
                        TimeSpan.FromSeconds(3.5),
                        TargetView: "settings",
                        Priority: ToastPriority.Feedback,
                        Category: "snap"));
                    TriggerLiquidImpulse(28, -10, 4);
                    break;
            }
        }
    }

    private void UpdateDropletsUI()
    {
        if (_dropletRegistry == null) return;
        var drp = SettingsContainer?.DropletsPanel;

        if (SettingsContainer?.SettingsRailDropletsCount != null)
        {
            SettingsContainer.SettingsRailDropletsCount.Text = _dropletRegistry.EnabledCount.ToString();
        }

        if (drp == null) return;

        // Update Category Filter Counts (No raw emojis)
        int totalCount = _dropletRegistry.Items.Count;
        int prodCount = _dropletRegistry.Items.Count(d => d.Category == Core.DropletCategory.Productivity);
        int mediaCount = _dropletRegistry.Items.Count(d => d.Category == Core.DropletCategory.Media);
        int sysCount = _dropletRegistry.Items.Count(d => d.Category == Core.DropletCategory.System);
        int capCount = _dropletRegistry.Items.Count(d => d.Category == Core.DropletCategory.Capture);

        if (drp.DropletFilterAllText != null) drp.DropletFilterAllText.Text = $"All ({totalCount})";
        if (drp.DropletFilterProdText != null) drp.DropletFilterProdText.Text = $"Work ({prodCount})";
        if (drp.DropletFilterMediaText != null) drp.DropletFilterMediaText.Text = $"Media ({mediaCount})";
        if (drp.DropletFilterSysText != null) drp.DropletFilterSysText.Text = $"Sys ({sysCount})";
        if (drp.DropletFilterCapText != null) drp.DropletFilterCapText.Text = $"Cap ({capCount})";

        // Filter active styling
        var activeBg = (Brush)FindResource("SettingsTabActiveBgBrush");
        var inactiveBg = Brushes.Transparent;
        var activeFg = (Brush)FindResource("PrimaryTextBrush");
        var inactiveFg = (Brush)FindResource("SecondaryBrush");

        void UpdateFilterBtn(Button? btn, TextBlock? text, bool isActive)
        {
            if (btn == null) return;
            btn.Background = isActive ? activeBg : inactiveBg;
            if (text != null)
            {
                text.Foreground = isActive ? activeFg : inactiveFg;
                text.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        UpdateFilterBtn(drp.DropletFilterAllBtn, drp.DropletFilterAllText, string.Equals(_activeDropletCategory, "All", StringComparison.OrdinalIgnoreCase));
        UpdateFilterBtn(drp.DropletFilterProdBtn, drp.DropletFilterProdText, string.Equals(_activeDropletCategory, "Productivity", StringComparison.OrdinalIgnoreCase));
        UpdateFilterBtn(drp.DropletFilterMediaBtn, drp.DropletFilterMediaText, string.Equals(_activeDropletCategory, "Media", StringComparison.OrdinalIgnoreCase));
        UpdateFilterBtn(drp.DropletFilterSysBtn, drp.DropletFilterSysText, string.Equals(_activeDropletCategory, "System", StringComparison.OrdinalIgnoreCase));
        UpdateFilterBtn(drp.DropletFilterCapBtn, drp.DropletFilterCapText, string.Equals(_activeDropletCategory, "Capture", StringComparison.OrdinalIgnoreCase));

        // Filter droplets collection
        IEnumerable<Core.DropletDescriptor> filtered = _dropletRegistry.Items;
        if (!string.Equals(_activeDropletCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(d => string.Equals(d.CategoryLabel, _activeDropletCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_dropletSearchFilter))
        {
            filtered = filtered.Where(d =>
                d.Title.Contains(_dropletSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                d.Subtitle.Contains(_dropletSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                d.CategoryLabel.Contains(_dropletSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                d.FeatureBadge.Contains(_dropletSearchFilter, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        if (drp.DropletsItemsControl != null)
        {
            drp.DropletsItemsControl.ItemsSource = list;
        }

        if (drp.DropletsEmptyPlaceholder != null)
        {
            drp.DropletsEmptyPlaceholder.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (drp.DropletsHeaderSummaryText != null)
        {
            drp.DropletsHeaderSummaryText.Text = $"{_dropletRegistry.EnabledCount} / {_dropletRegistry.Items.Count} Active · ~{_dropletRegistry.TotalActiveRamKb} KB";
        }
    }
}
