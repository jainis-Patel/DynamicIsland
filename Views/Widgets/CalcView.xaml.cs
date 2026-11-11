using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class CalcView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Launcher);

    public CalcView()
    {
        InitializeComponent();
    }

    private void CalcInputBox_KeyDown(object sender, KeyEventArgs e) =>
        Island?.CalcInputBox_KeyDown(sender, e);

    private void CalcInputBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Island?.CalcInputBox_TextChanged(sender, e);

    private void CalcResult_Click(object sender, MouseButtonEventArgs e) =>
        Island?.CalcResult_Click(sender, e);

    private void CalcHistoryChip_Click(object sender, RoutedEventArgs e) =>
        Island?.CalcHistoryChip_Click(sender, e);

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.HorizontalScrollViewer_PreviewMouseWheel(sender, e);

}
