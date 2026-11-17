using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class QuickReplyView : UserControl
{
    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;
    private NotificationHistoryItem? _currentItem;

    public QuickReplyView()
    {
        InitializeComponent();
    }

    public void SetNotification(NotificationHistoryItem item)
    {
        _currentItem = item;
        ContactNameText.Text = $"Reply to {item.Title}";
        AppBadgeText.Text = string.IsNullOrWhiteSpace(item.AppName) ? "Message" : item.AppName;
        QuotedMessageText.Text = string.IsNullOrWhiteSpace(item.Body) ? item.Title : item.Body;

        AppGlyphText.Text = item.Glyph;
        AppGlyphText.Foreground = item.AccentBrush;
        AppIconBorder.Background = item.IconBgBrush;

        ReplyTextBox.Text = string.Empty;
        PlaceholderText.Visibility = Visibility.Visible;
    }

    public void FocusInput()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            ReplyTextBox.Focus();
            ReplyTextBox.SelectAll();
        });
    }

    private void ReplyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrEmpty(ReplyTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ReplyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Ctrl+Enter opens the target app
                OpenAppButton_Click(sender, e);
            }
            else
            {
                // Enter sends the reply
                SendReply();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        SendReply();
    }

    private void SendReply()
    {
        string text = ReplyTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || _currentItem == null) return;

        var item = _currentItem;
        SendButton.IsEnabled = false;
        Island?.SendQuickReplyAsync(item, text).ContinueWith(t =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                SendButton.IsEnabled = true;
                if (t.IsCompletedSuccessfully && t.Result)
                {
                    ReplyTextBox.Text = string.Empty;
                }
            });
        });
    }

    private void OpenAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentItem != null)
        {
            string pendingText = ReplyTextBox.Text?.Trim() ?? string.Empty;
            Island?.OpenTargetApp(_currentItem, pendingText);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Island?.CancelQuickReply();
    }
}
