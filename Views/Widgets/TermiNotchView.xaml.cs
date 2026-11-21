using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class TermiNotchView : UserControl
{
    private Action<string>? _activeDiffDecisionCallback;
    private bool _isDiffActive;
    public bool IsDiffActive => _isDiffActive;

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
            if (ConsoleOutputBox.Text.Length > 32768)
            {
                ConsoleOutputBox.Text = ConsoleOutputBox.Text[^24576..];
                ConsoleOutputBox.CaretIndex = ConsoleOutputBox.Text.Length;
            }
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
            if (svc.IsRunning) return;
            RunCommandBtn.IsEnabled = false;
            TermiStatusText.Text = "Running...";
            try
            {
                await svc.RunSnippetAsync(id);
            }
            catch (Exception ex)
            {
                App.Log($"[TermiNotchView] Snippet run failed: {ex.Message}");
                TermiStatusText.Text = "Failed";
                RunCommandBtn.IsEnabled = true;
            }
        }
    }

    private async void RunCommandBtn_Click(object sender, RoutedEventArgs e)
    {
        string cmd = CommandInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(cmd)) return;

        if (Island?.TermiNotchServiceInstance is { } svc)
        {
            if (svc.IsRunning) return;
            CommandInputBox.Clear();
            RunCommandBtn.IsEnabled = false;
            TermiStatusText.Text = "Running...";
            try
            {
                await svc.RunRawCommandAsync(cmd, "powershell");
            }
            catch (Exception ex)
            {
                App.Log($"[TermiNotchView] Command run failed: {ex.Message}");
                TermiStatusText.Text = "Failed";
                RunCommandBtn.IsEnabled = true;
            }
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

    // ── AI Coding Agent Diff HUD & Interactive Approvals ───────────────

    public static (string Filename, int Added, int Removed, int Chunks) ParseDiffMetrics(string diffText)
    {
        if (string.IsNullOrWhiteSpace(diffText))
            return ("code.diff", 0, 0, 0);

        string filename = "code.diff";
        int added = 0;
        int removed = 0;
        int chunks = 0;

        var lines = diffText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            if (string.IsNullOrEmpty(rawLine)) continue;

            if (rawLine.StartsWith("+++ b/") || rawLine.StartsWith("+++ "))
            {
                int spaceIdx = rawLine.IndexOf(' ');
                if (spaceIdx >= 0)
                {
                    string candidate = rawLine[(spaceIdx + 1)..].Trim();
                    if (candidate.StartsWith("b/", StringComparison.Ordinal))
                        candidate = candidate[2..];
                    else if (candidate.StartsWith("a/", StringComparison.Ordinal))
                        candidate = candidate[2..];
                    filename = candidate;
                }
            }

            if (rawLine.StartsWith("+") && !rawLine.StartsWith("+++")) added++;
            else if (rawLine.StartsWith("-") && !rawLine.StartsWith("---")) removed++;
            else if (rawLine.StartsWith("@@")) chunks++;
        }

        return (filename, added, removed, chunks);
    }

    public void StageDiff(string title, string? subtitle, string diffText, Action<string> onDecision)
    {
        _activeDiffDecisionCallback = onDecision;
        _isDiffActive = true;

        Dispatcher.InvokeAsync(() =>
        {
            TermiHeaderTitle.Text = string.IsNullOrWhiteSpace(title) ? "AI Agent Diff Review" : title;
            TermiDiffBadge.Visibility = Visibility.Visible;
            TermiDiffBadgeText.Text = "AI Diff Review";

            var (filename, added, removed, chunks) = ParseDiffMetrics(diffText);

            DiffLinesHost.Children.Clear();

            var lines = diffText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var rawLine in lines)
            {
                if (string.IsNullOrEmpty(rawLine)) continue;

                bool isAdd = rawLine.StartsWith("+") && !rawLine.StartsWith("+++");
                bool isDel = rawLine.StartsWith("-") && !rawLine.StartsWith("---");
                bool isChunk = rawLine.StartsWith("@@");
                bool isHeader = rawLine.StartsWith("diff ") || rawLine.StartsWith("index ") || rawLine.StartsWith("---") || rawLine.StartsWith("+++");

                Brush bg = Brushes.Transparent;
                Brush fg = (Brush)FindResource("PrimaryTextBrush");
                FontWeight weight = FontWeights.Normal;
                FontStyle style = FontStyles.Normal;

                if (isAdd)
                {
                    bg = ThemeBrushes.CreateFrozen(Color.FromArgb(0x22, 0x30, 0xD1, 0x58));
                    fg = ThemeBrushes.CreateFrozen(Color.FromRgb(0x30, 0xD1, 0x58));
                }
                else if (isDel)
                {
                    bg = ThemeBrushes.CreateFrozen(Color.FromArgb(0x22, 0xFF, 0x45, 0x3A));
                    fg = ThemeBrushes.CreateFrozen(Color.FromRgb(0xFF, 0x45, 0x3A));
                }
                else if (isChunk)
                {
                    bg = ThemeBrushes.CreateFrozen(Color.FromArgb(0x25, 0x0A, 0x84, 0xFF));
                    fg = ThemeBrushes.CreateFrozen(Color.FromRgb(0x0A, 0x84, 0xFF));
                    weight = FontWeights.SemiBold;
                }
                else if (isHeader)
                {
                    fg = (Brush)FindResource("SecondaryBrush");
                    style = FontStyles.Italic;
                }

                var border = new Border
                {
                    Background = bg,
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0.5, 0, 0.5),
                    CornerRadius = new CornerRadius(2)
                };

                var tb = new TextBlock
                {
                    Text = rawLine,
                    Foreground = fg,
                    FontFamily = new FontFamily("Consolas, Cascadia Code, Courier New"),
                    FontSize = 9.0,
                    FontWeight = weight,
                    FontStyle = style,
                    TextWrapping = TextWrapping.Wrap
                };

                border.Child = tb;
                DiffLinesHost.Children.Add(border);
            }

            DiffFileLabel.Text = filename;
            DiffStatsAddedText.Text = $"+{added}";
            DiffStatsRemovedText.Text = $"-{removed}";
            TermiStatusText.Text = $"{filename} · +{added} / -{removed} lines";

            ShowDiffMode();
        });
    }

    public void ShowDiffMode()
    {
        ConsoleScrollViewer.Visibility = Visibility.Collapsed;
        ConsoleInputPanel.Visibility = Visibility.Collapsed;
        SnippetsScrollViewer.Visibility = Visibility.Collapsed;

        DiffScrollViewer.Visibility = Visibility.Visible;
        DiffDecisionPanel.Visibility = Visibility.Visible;
    }

    public void ShowConsoleMode()
    {
        DiffScrollViewer.Visibility = Visibility.Collapsed;
        DiffDecisionPanel.Visibility = Visibility.Collapsed;

        ConsoleScrollViewer.Visibility = Visibility.Visible;
        ConsoleInputPanel.Visibility = Visibility.Visible;
        SnippetsScrollViewer.Visibility = Visibility.Visible;

        TermiHeaderTitle.Text = "Termi-Notch Console";
        TermiStatusText.Text = "PowerShell / CMD · 0% CPU Idle";
    }

    private void DiffApproveBtn_Click(object sender, RoutedEventArgs e)
    {
        TactileFeedback.PlaySnap();
        var cb = _activeDiffDecisionCallback;
        _activeDiffDecisionCallback = null;
        _isDiffActive = false;
        TermiDiffBadge.Visibility = Visibility.Collapsed;

        cb?.Invoke("Approve");
        ShowConsoleMode();
        ConsoleOutputBox.AppendText("\n[AI Agent] ✓ Change approved by user.\n");
        ConsoleScrollViewer.ScrollToEnd();
    }

    private void DiffRejectBtn_Click(object sender, RoutedEventArgs e)
    {
        TactileFeedback.PlaySnap();
        var cb = _activeDiffDecisionCallback;
        _activeDiffDecisionCallback = null;
        _isDiffActive = false;
        TermiDiffBadge.Visibility = Visibility.Collapsed;

        cb?.Invoke("Reject");
        ShowConsoleMode();
        ConsoleOutputBox.AppendText("\n[AI Agent] ✕ Change rejected by user.\n");
        ConsoleScrollViewer.ScrollToEnd();
    }

    private void DiffBackBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowConsoleMode();
    }

    private void TermiDiffBadge_Click(object sender, MouseButtonEventArgs e)
    {
        if (DiffScrollViewer.Visibility == Visibility.Visible)
        {
            ShowConsoleMode();
        }
        else
        {
            ShowDiffMode();
        }
        e.Handled = true;
    }
}
