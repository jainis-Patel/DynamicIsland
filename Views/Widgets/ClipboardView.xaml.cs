using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Views.Widgets;

public partial class ClipboardView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void SwitchToShelf_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Shelf);

    public ClipboardView()
    {
        InitializeComponent();
    }

    private void ClearClipboardButton_Click(object sender, RoutedEventArgs e) =>
        Island?.ClearClipboardButton_Click(sender, e);

    private void ClearClipboardSearch_Click(object sender, RoutedEventArgs e) =>
        Island?.ClearClipboardSearch_Click(sender, e);

    private void ClipboardItem_Click(object sender, MouseButtonEventArgs e) =>
        Island?.ClipboardItem_Click(sender, e);

    private void ClipboardScrollLeft_Click(object sender, RoutedEventArgs e) =>
        Island?.ClipboardScrollLeft_Click(sender, e);

    private void ClipboardScrollRight_Click(object sender, RoutedEventArgs e) =>
        Island?.ClipboardScrollRight_Click(sender, e);

    private void ClipboardSearchBox_KeyDown(object sender, KeyEventArgs e) =>
        Island?.ClipboardSearchBox_KeyDown(sender, e);

    private void ClipboardSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Island?.ClipboardSearchBox_TextChanged(sender, e);

    private void ClipFilter_Click(object sender, RoutedEventArgs e) =>
        Island?.ClipFilter_Click(sender, e);

    private void CopyColorAsRgb_Click(object sender, RoutedEventArgs e) =>
        Island?.CopyColorAsRgb_Click(sender, e);

    private void CopyPlainClipboardItem_Click(object sender, RoutedEventArgs e) =>
        Island?.CopyPlainClipboardItem_Click(sender, e);

    private void TransformClipboardItem_Click(object sender, RoutedEventArgs e) =>
        Island?.TransformClipboardItem_Click(sender, e);

    private void EyedropperButton_Click(object sender, RoutedEventArgs e) =>
        Island?.EyedropperButton_Click(sender, e);

    private void CaptureRegionButton_Click(object sender, RoutedEventArgs e) =>
        Island?.CaptureRegionButton_Click(sender, e);

    private void ClipboardQuickPickerButton_Click(object sender, RoutedEventArgs e) =>
        Island?.ClipboardQuickPickerButton_Click(sender, e);

    private void EnqueueClipboardItem_Click(object sender, RoutedEventArgs e) =>
        Island?.EnqueueClipboardItem_Click(sender, e);

    private void OcrCardItem_Click(object sender, RoutedEventArgs e) =>
        Island?.OcrCardItem_Click(sender, e);

    private void OcrClipboardButton_Click(object sender, RoutedEventArgs e) =>
        Island?.OcrClipboardButton_Click(sender, e);

    private void OpenUrlClipboardItem_Click(object sender, RoutedEventArgs e) =>
        Island?.OpenUrlClipboardItem_Click(sender, e);

    private void PinClipboardItem_Click(object sender, RoutedEventArgs e) =>
        Island?.PinClipboardItem_Click(sender, e);

    private void RemoveClipboardItem_Click(object sender, RoutedEventArgs e) =>
        Island?.RemoveClipboardItem_Click(sender, e);

    private void ScrollViewer_MouseUp(object sender, MouseButtonEventArgs e) =>
        Island?.ScrollViewer_MouseUp(sender, e);

    private void SendClipboardItemToShelf_Click(object sender, RoutedEventArgs e) =>
        Island?.SendClipboardItemToShelf_Click(sender, e);

    private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        Island?.HorizontalScrollViewer_PreviewMouseWheel(sender, e);

}
