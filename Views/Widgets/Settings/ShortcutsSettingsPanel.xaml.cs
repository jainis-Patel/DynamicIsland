using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets.Settings;

public partial class ShortcutsSettingsPanel : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public ShortcutsSettingsPanel()
    {
        InitializeComponent();
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void HotkeyRecordBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HotkeyRecordBtn_Click(sender, e);

    private void VolumeOsdBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.VolumeOsdBtn_Click(sender, e);

    private void LockKeyHudToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.LockKeyHudToggleBtn_Click(sender, e);

    private void KeyboardLayoutHudToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.KeyboardLayoutHudToggleBtn_Click(sender, e);

    private void DevHotkeysToggleBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.DevHotkeysToggleBtn_Click(sender, e);
}
