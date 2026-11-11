using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class ShelfView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void SwitchToClipboard_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Clipboard);

    public ShelfView()
    {
        InitializeComponent();
    }

    private void BrowseToShelf_Click(object sender, RoutedEventArgs e) =>
        Island?.BrowseToShelf_Click(sender, e);

    private void ClearShelfButton_Click(object sender, RoutedEventArgs e) =>
        Island?.ClearShelfButton_Click(sender, e);

    private void CopyAllShelfButton_Click(object sender, RoutedEventArgs e) =>
        Island?.CopyAllShelfButton_Click(sender, e);

    private void CopyShelfFile_Click(object sender, RoutedEventArgs e) =>
        Island?.CopyShelfFile_Click(sender, e);

    private void CopyShelfPath_Click(object sender, RoutedEventArgs e) =>
        Island?.CopyShelfPath_Click(sender, e);

    private void DragAllShelf_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        Island?.DragAllShelf_MouseLeftButtonDown(sender, e);

    private void DragAllShelf_MouseMove(object sender, MouseEventArgs e) =>
        Island?.DragAllShelf_MouseMove(sender, e);

    private void PasteToShelf_Click(object sender, RoutedEventArgs e) =>
        Island?.PasteToShelf_Click(sender, e);

    private void RemoveShelfItem_Click(object sender, RoutedEventArgs e) =>
        Island?.RemoveShelfItem_Click(sender, e);

    private void RevealShelfItem_Click(object sender, RoutedEventArgs e) =>
        Island?.RevealShelfItem_Click(sender, e);

    private void ScrollViewer_MouseUp(object sender, MouseButtonEventArgs e) =>
        Island?.ScrollViewer_MouseUp(sender, e);

    private void ShelfItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        Island?.ShelfItem_MouseLeftButtonDown(sender, e);

    private void ShelfItem_MouseMove(object sender, MouseEventArgs e) =>
        Island?.ShelfItem_MouseMove(sender, e);

    private void ShelfScrollLeft_Click(object sender, RoutedEventArgs e) =>
        Island?.ShelfScrollLeft_Click(sender, e);

    private void ShelfScrollRight_Click(object sender, RoutedEventArgs e) =>
        Island?.ShelfScrollRight_Click(sender, e);

    private void ZipShelfButton_Click(object sender, RoutedEventArgs e) =>
        Island?.ZipShelfButton_Click(sender, e);

    private void WindowsShareButton_Click(object sender, RoutedEventArgs e) =>
        Island?.WindowsShareButton_Click(sender, e);

    private void PhoneShareButton_Click(object sender, RoutedEventArgs e) =>
        Island?.PhoneShareButton_Click(sender, e);

    private void ItemWindowsShare_Click(object sender, RoutedEventArgs e) =>
        Island?.ItemWindowsShare_Click(sender, e);

    private void ItemOcr_Click(object sender, RoutedEventArgs e) =>
        Island?.ItemOcr_Click(sender, e);

    private void ShelfActionsButton_Click(object sender, RoutedEventArgs e) =>
        Island?.ShelfActionsButton_Click(sender, e);

    private void SummonBasketButton_Click(object sender, RoutedEventArgs e) =>
        Island?.SummonBasketButton_Click(sender, e);

    private void PinShelfItem_Click(object sender, RoutedEventArgs e) =>
        Island?.PinShelfItem_Click(sender, e);

    private void ShelfItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e) =>
        Island?.ShelfItem_MouseRightButtonUp(sender, e);

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.HorizontalScrollViewer_PreviewMouseWheel(sender, e);

}
