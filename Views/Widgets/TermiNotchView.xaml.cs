using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class TermiNotchView : UserControl
{
    public TermiNotchView()
    {
        InitializeComponent();
        Loaded += TermiNotchView_Loaded;
        Unloaded += TermiNotchView_Unloaded;
    }

    public void FocusInput()
    {
        CommandInputBox?.Focus();
        CommandInputBox?.SelectAll();
    }

    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Launcher);

    private void TermiNotchView_Loaded(object sender, RoutedEventArgs e)
    {
        if (Island?.TermiNotchServiceInstance is { } svc)
        {
            svc.OutputReceived -= OnOutputReceived;
            svc.OutputReceived += OnOutputReceived;
            svc.ExecutionCompleted -= OnExecutionCompleted;
            svc.ExecutionCompleted += OnExecutionCompleted;
        }
        PopulateSnippetPills();
    }

    private void TermiNotchView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (Island?.TermiNotchServiceInstance is { } svc)
        {
            svc.OutputReceived -= OnOutputReceived;
            svc.ExecutionCompleted -= OnExecutionCompleted;
        }
    }

    private void OnOutputReceived(string line, bool isError)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ConsoleOutputBox.AppendText($"{line}\n");
            ConsoleScrollViewer.ScrollToEnd();
        });
    }

    private void OnExecutionCompleted(bool success, int exitCode)
    {
        Dispatcher.InvokeAsync(() =>
        {
            TermiStatusText.Text = $"Exit code: {exitCode} · 0% CPU Idle";
            RunCommandBtn.IsEnabled = true;
        });
    }

    public void PopulateSnippetPills()
    {
        SnippetsPillStack.Children.Clear();
        if (Island?.TermiNotchServiceInstance is { } svc)
        {
            foreach (var snip in svc.Snippets)
            {
                var btn = new Button
                {
                    Style = (Style)FindResource("IslandButton"),
                    Height = 21,
                    Padding = new Thickness(7, 0, 7, 0),
                    Margin = new Thickness(0, 0, 3, 0),
                    Tag = snip.Id,
                    Content = new TextBlock
                    {
                        Text = snip.Name,
                        FontSize = 8.5,
                        FontWeight = FontWeights.Medium
                    }
                };
                btn.Click += SnippetPill_Click;
                SnippetsPillStack.Children.Add(btn);
            }
        }
    }

    private async void SnippetPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && Island?.TermiNotchServiceInstance is { } svc)
        {
            RunCommandBtn.IsEnabled = false;
            TermiStatusText.Text = "Running...";
            await svc.RunSnippetAsync(id);
        }
    }

    private async void RunCommandBtn_Click(object sender, RoutedEventArgs e)
    {
        string cmd = CommandInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(cmd)) return;

        CommandInputBox.Clear();
        if (Island?.TermiNotchServiceInstance is { } svc)
        {
            RunCommandBtn.IsEnabled = false;
            TermiStatusText.Text = "Running...";
            await svc.RunRawCommandAsync(cmd, "powershell");
        }
    }

    private void CommandInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunCommandBtn_Click(sender, e);
        }
    }

    private void ClearConsoleBtn_Click(object sender, RoutedEventArgs e)
    {
        ConsoleOutputBox.Text = "Dynamic Island Termi-Notch Console [Cleared]\n";
    }
}
