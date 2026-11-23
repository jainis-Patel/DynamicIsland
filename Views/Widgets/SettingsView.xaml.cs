using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Core;

namespace DynamicIsland.Views.Widgets;

public partial class SettingsView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    public SettingsView()
    {
        InitializeComponent();
    }

    internal ScrollViewer ActiveScrollViewer
    {
        get
        {
            if (SettingsSearchResultsHost.Visibility == Visibility.Visible)
                return SettingsSearchResultsScroll;

            if (FloraPanel.Visibility == Visibility.Visible) return FloraPanel.PanelScrollViewer;
            if (ClockAudioPanel.Visibility == Visibility.Visible) return ClockAudioPanel.PanelScrollViewer;
            if (DropletsPanel.Visibility == Visibility.Visible) return DropletsPanel.PanelScrollViewer;
            if (DockPanel.Visibility == Visibility.Visible) return DockPanel.PanelScrollViewer;
            if (DisplayPowerPanel.Visibility == Visibility.Visible) return DisplayPowerPanel.PanelScrollViewer;
            if (NotificationsPanel.Visibility == Visibility.Visible) return NotificationsPanel.PanelScrollViewer;
            if (PetPanel.Visibility == Visibility.Visible) return PetPanel.PanelScrollViewer;
            if (ShortcutsPanel.Visibility == Visibility.Visible) return ShortcutsPanel.PanelScrollViewer;
            if (SystemUpdatesPanel.Visibility == Visibility.Visible) return SystemUpdatesPanel.PanelScrollViewer;
            return AppearancePanel.PanelScrollViewer;
        }
    }

    internal void FocusSearch()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            Keyboard.Focus(SettingsSearchBox);
            SettingsSearchBox.Focus();
            SettingsSearchBox.SelectAll();
        }));
    }

    internal bool ClearSearchIfActive()
    {
        bool hadText = !string.IsNullOrEmpty(SettingsSearchBox.Text);
        bool hadFocus = SettingsSearchBox.IsKeyboardFocusWithin;
        if (!hadText && !hadFocus) return false;

        SettingsSearchBox.Clear();
        if (hadFocus) Keyboard.ClearFocus();
        return true;
    }

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SettingsSearchBox.Text ?? "";
        bool hasQuery = !string.IsNullOrWhiteSpace(query);

        SettingsSearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
        SettingsSearchClearBtn.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

        if (!hasQuery)
        {
            SettingsSearchResultsHost.Visibility = Visibility.Collapsed;
            SettingsSearchResultsList.ItemsSource = null;
            return;
        }

        var results = SettingsSearchIndex.Search(query);
        SettingsSearchResultsList.ItemsSource = results;
        SettingsSearchEmptyPlaceholder.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsSearchResultsHost.Visibility = Visibility.Visible;
        SettingsSearchResultsScroll.ScrollToVerticalOffset(0);
    }

    private void SettingsSearchClearBtn_Click(object sender, RoutedEventArgs e)
    {
        SettingsSearchBox.Clear();
        Keyboard.ClearFocus();
    }

    private void SearchResultItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string categoryKey })
        {
            SettingsSearchBox.Clear();
            Keyboard.ClearFocus();
            Island?.SwitchSettingsTab(categoryKey, impulse: true);
        }
    }

    private void VerticalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.VerticalScrollViewer_PreviewMouseWheel(sender, e);

    private void SettingsTabBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SettingsSearchBox.Text))
        {
            SettingsSearchBox.Clear();
        }
        Island?.SettingsTabBtn_Click(sender, e);
    }

    private void SettingsTrimRamBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.SettingsTrimRamBtn_Click(sender, e);

    private void SettingsOpenDataBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.SettingsOpenDataBtn_Click(sender, e);
}
