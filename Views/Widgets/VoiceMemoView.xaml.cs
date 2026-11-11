using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class VoiceMemoView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Launcher);

    public VoiceMemoView()
    {
        InitializeComponent();
    }

    private void HeroRecordBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.HeroRecordBtn_Click(sender, e);

    private void MemoPlayPauseBtn_Click(object sender, RoutedEventArgs e) =>
        Island?.MemoPlayPauseBtn_Click(sender, e);

    private void OpenMemosFolder_Click(object sender, RoutedEventArgs e) =>
        Island?.OpenMemosFolder_Click(sender, e);

    private void SendMemoToShelf_Click(object sender, RoutedEventArgs e) =>
        Island?.SendMemoToShelf_Click(sender, e);

    private void PlayMemoItem_Click(object sender, RoutedEventArgs e) =>
        Island?.PlayMemoItem_Click(sender, e);

    private void CopyMemoItem_Click(object sender, RoutedEventArgs e) =>
        Island?.CopyMemoItem_Click(sender, e);

    private void SendMemoItemToShelf_Click(object sender, RoutedEventArgs e) =>
        Island?.SendMemoItemToShelf_Click(sender, e);

    private void DeleteMemoItem_Click(object sender, RoutedEventArgs e) =>
        Island?.DeleteMemoItem_Click(sender, e);

}
