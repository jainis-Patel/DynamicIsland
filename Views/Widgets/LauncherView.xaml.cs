using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public sealed class LauncherDisplayItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string TargetPath { get; init; } = "";
    public string Glyph { get; init; } = "\uE71D";
    public string Subtitle { get; init; } = "";
    public string FriendlyDescription { get; init; } = "";
    public string ActionHint { get; init; } = "Click to launch · Right-click for options";
    public ImageSource? IconSource { get; init; }
    public bool HasIcon => IconSource != null;
    public bool IsUserPinned { get; init; }
    public bool IsFolder { get; init; }
    public Brush AccentBrush { get; init; } = ThemeBrushes.AmberDark;
    public Brush IconBgBrush { get; init; } = ThemeBrushes.DockInactiveBgDark;
    public Brush IconBorderBrush { get; init; } = ThemeBrushes.CardBorderDark;
}

public partial class LauncherView : UserControl
{
    // High-performance frozen palette brushes for categories
    private static readonly SolidColorBrush UnpinRedBrush       = CreateFrozenBrush("#FF453A");
    private static readonly SolidColorBrush TabActiveBg         = CreateFrozenBrush("#2EFFFFFF");
    private static readonly SolidColorBrush TabActiveFg         = CreateFrozenBrush("#FFFFFF");
    private static readonly SolidColorBrush TabInactiveBg       = CreateFrozenBrush("#12FFFFFF");
    private static readonly SolidColorBrush TabInactiveFg       = CreateFrozenBrush("#9E9EA6");

    private static readonly SolidColorBrush GreenAccent         = CreateFrozenBrush("#30D158");
    private static readonly SolidColorBrush GreenBg             = CreateFrozenBrush("#2230D158");
    private static readonly SolidColorBrush GreenBorder         = CreateFrozenBrush("#3330D158");

    private static readonly SolidColorBrush BlueAccent          = CreateFrozenBrush("#0A84FF");
    private static readonly SolidColorBrush BlueBg              = CreateFrozenBrush("#220A84FF");
    private static readonly SolidColorBrush BlueBorder          = CreateFrozenBrush("#330A84FF");

    private static readonly SolidColorBrush YellowAccent        = CreateFrozenBrush("#FFD60A");
    private static readonly SolidColorBrush YellowBg            = CreateFrozenBrush("#22FFD60A");
    private static readonly SolidColorBrush YellowBorder        = CreateFrozenBrush("#33FFD60A");

    private static readonly SolidColorBrush PurpleAccent        = CreateFrozenBrush("#BF5AF2");
    private static readonly SolidColorBrush PurpleBg            = CreateFrozenBrush("#22BF5AF2");
    private static readonly SolidColorBrush PurpleBorder        = CreateFrozenBrush("#33BF5AF2");

    private static readonly SolidColorBrush OrangeAccent        = CreateFrozenBrush("#FF9F0A");
    private static readonly SolidColorBrush OrangeBg            = CreateFrozenBrush("#22FF9F0A");
    private static readonly SolidColorBrush OrangeBorder        = CreateFrozenBrush("#33FF9F0A");

    private static readonly SolidColorBrush CyanAccent          = CreateFrozenBrush("#64D2FF");
    private static readonly SolidColorBrush CyanBg              = CreateFrozenBrush("#2264D2FF");
    private static readonly SolidColorBrush CyanBorder          = CreateFrozenBrush("#3364D2FF");

    private static readonly SolidColorBrush PinkAccent          = CreateFrozenBrush("#FF375F");
    private static readonly SolidColorBrush PinkBg              = CreateFrozenBrush("#22FF375F");
    private static readonly SolidColorBrush PinkBorder          = CreateFrozenBrush("#33FF375F");

    private static readonly SolidColorBrush RedAccent           = CreateFrozenBrush("#FF453A");
    private static readonly SolidColorBrush RedBg               = CreateFrozenBrush("#22FF453A");
    private static readonly SolidColorBrush RedBorder           = CreateFrozenBrush("#33FF453A");

    private static readonly SolidColorBrush AppDefaultBg        = CreateFrozenBrush("#18FFFFFF");
    private static readonly SolidColorBrush AppDefaultBorder    = CreateFrozenBrush("#26FFFFFF");

    private enum LauncherFilterMode { All, Pinned, Folders }
    private LauncherFilterMode _currentFilter = LauncherFilterMode.All;
    private string _searchQuery = "";

    private List<LauncherDisplayItem> _allCombined = new();
    private List<LauncherDisplayItem> _pinnedOnly = new();
    private List<LauncherDisplayItem> _foldersOnly = new();

    public LauncherView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Island?.QuickLauncherServiceInstance is { } svc)
        {
            svc.Changed -= OnLauncherServiceChanged;
            svc.Changed += OnLauncherServiceChanged;
        }
        RefreshItems();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Island?.QuickLauncherServiceInstance is { } svc)
        {
            svc.Changed -= OnLauncherServiceChanged;
        }
    }

    private void OnLauncherServiceChanged()
    {
        Dispatcher.InvokeAsync(RefreshItems);
    }

    public void FocusSearch()
    {
        SearchInputBox?.Focus();
        SearchInputBox?.SelectAll();
    }

    private IslandWindow? Island => IslandWindow.Instance ?? (Window.GetWindow(this) as IslandWindow) ?? (Application.Current?.MainWindow as IslandWindow);

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void TabAll_Click(object sender, RoutedEventArgs e)
    {
        _currentFilter = LauncherFilterMode.All;
        ApplyFilter();
    }

    private void TabPinned_Click(object sender, RoutedEventArgs e)
    {
        _currentFilter = LauncherFilterMode.Pinned;
        ApplyFilter();
    }

    private void TabWidgets_Click(object sender, RoutedEventArgs e)
    {
        _currentFilter = LauncherFilterMode.Folders;
        ApplyFilter();
    }

    private void SearchInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchInputBox.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchBtn.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Collapsed : Visibility.Visible;
        ApplyFilter();
    }

    private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        SearchInputBox.Text = "";
        SearchInputBox.Focus();
    }

    private void ApplyFilter()
    {
        IEnumerable<LauncherDisplayItem> source = _currentFilter switch
        {
            LauncherFilterMode.Pinned => _pinnedOnly.Where(i => !i.IsFolder).ToList(),
            LauncherFilterMode.Folders => _foldersOnly,
            _ => _allCombined
        };

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            source = source.Where(item =>
                item.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                item.Subtitle.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                item.FriendlyDescription.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));
        }

        var list = source.ToList();
        LauncherItemsControl.ItemsSource = list;

        bool hasSearch = !string.IsNullOrWhiteSpace(_searchQuery);
        EmptySearchPrompt.Visibility = (hasSearch && list.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        EmptyAppsPrompt.Visibility = (!hasSearch && _pinnedOnly.Count == 0)
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateFilterUi();
    }

    private void UpdateFilterUi()
    {
        if (TabAllBtn == null || TabPinnedBtn == null || TabWidgetsBtn == null) return;

        TabAllBtn.Background = _currentFilter == LauncherFilterMode.All ? TabActiveBg : TabInactiveBg;
        TabAllBtn.Foreground = _currentFilter == LauncherFilterMode.All ? TabActiveFg : TabInactiveFg;

        TabPinnedBtn.Background = _currentFilter == LauncherFilterMode.Pinned ? TabActiveBg : TabInactiveBg;
        TabPinnedBtn.Foreground = _currentFilter == LauncherFilterMode.Pinned ? TabActiveFg : TabInactiveFg;

        TabWidgetsBtn.Background = _currentFilter == LauncherFilterMode.Folders ? TabActiveBg : TabInactiveBg;
        TabWidgetsBtn.Foreground = _currentFilter == LauncherFilterMode.Folders ? TabActiveFg : TabInactiveFg;
    }

    public static ImageSource? GetAppIcon(string? targetPath) =>
        AppIconLoader.GetAppIcon(targetPath);

    private static string? ResolveExePath(string? path) =>
        AppIconLoader.ResolveExePath(path);

    public void RefreshItems()
    {
        if (Island?.QuickLauncherServiceInstance is { } svc)
        {
            _pinnedOnly = svc.Items.Select(i =>
            {
                var icon = !i.IsFolder ? GetAppIcon(i.TargetPath) : null;
                string subtitle = i.LaunchCount == 1 ? "1 launch" : (i.LaunchCount > 1 ? $"{i.LaunchCount} launches" : (i.IsFolder ? "Folder" : "Application"));
                string desc = i.IsFolder ? $"Folder · {i.TargetPath}" : $"Application · {i.TargetPath}";
                return new LauncherDisplayItem
                {
                    Id = i.Id,
                    Name = i.Name,
                    TargetPath = i.TargetPath,
                    Glyph = i.Glyph,
                    Subtitle = subtitle,
                    FriendlyDescription = desc,
                    ActionHint = "Click to launch · Right-click for options",
                    IconSource = icon,
                    IsUserPinned = true,
                    IsFolder = i.IsFolder,
                    AccentBrush = OrangeAccent,
                    IconBgBrush = AppDefaultBg,
                    IconBorderBrush = AppDefaultBorder
                };
            }).ToList();
        }
        else
        {
            _pinnedOnly = new List<LauncherDisplayItem>();
        }

        // Pure desktop application items only (all widgets migrated to Bento Widget Gallery)
        _allCombined = _pinnedOnly;
        _foldersOnly = _pinnedOnly.Where(i => i.IsFolder).ToList();

        TabAllBtn.Content = $"All ({_allCombined.Count})";
        TabPinnedBtn.Content = $"Apps ({_pinnedOnly.Count - _foldersOnly.Count})";
        TabWidgetsBtn.Content = $"Folders ({_foldersOnly.Count})";

        ApplyFilter();
    }

    private void LaunchItem(LauncherDisplayItem item)
    {
        if (item.TargetPath.StartsWith("island:"))
        {
            string key = item.TargetPath.Substring(7);
            if (ExpandedViewExtensions.TryParseKey(key, out var view))
            {
                Island?.NavigateToView(view);
                return;
            }
        }

        bool ok = Island?.QuickLauncherServiceInstance?.Launch(item.Id) ?? false;
        Island?.PushNotificationToast(item.Glyph, "Launcher", ok ? $"Launched {item.Name}" : $"Failed to launch {item.Name}", ThemeBrushes.GreenColor);
        RefreshItems();
    }

    private void AppCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LauncherDisplayItem item })
        {
            LaunchItem(item);
        }
    }

    private void AppCard_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LauncherDisplayItem item)
        {
            var cm = new ContextMenu { Style = (Style)FindResource("IslandContextMenu") };

            var openItem = new MenuItem { Header = $"Launch {item.Name}", Style = (Style)FindResource("IslandMenuItem") };
            openItem.Click += (_, _) => LaunchItem(item);
            cm.Items.Add(openItem);

            if (item.IsUserPinned)
            {
                if (item.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var adminItem = new MenuItem { Header = "Run as Administrator", Style = (Style)FindResource("IslandMenuItem") };
                    adminItem.Click += (_, _) => LaunchAsAdmin(item);
                    cm.Items.Add(adminItem);
                }

                var locItem = new MenuItem { Header = "Open File Location", Style = (Style)FindResource("IslandMenuItem") };
                locItem.Click += (_, _) => OpenLocation(item);
                cm.Items.Add(locItem);

                cm.Items.Add(new Separator { Background = ThemeBrushes.MenuSeparator, Margin = new Thickness(2, 3, 2, 3) });

                var unpinItem = new MenuItem { Header = "Unpin from Launcher", Style = (Style)FindResource("IslandMenuItem"), Foreground = UnpinRedBrush };
                unpinItem.Click += (_, _) => UnpinItem(item);
                cm.Items.Add(unpinItem);
            }

            cm.PlacementTarget = fe;
            cm.IsOpen = true;
            e.Handled = true;
        }
    }

    private static void LaunchAsAdmin(LauncherDisplayItem item)
    {
        try
        {
            string? file = ResolveExePath(item.TargetPath);
            if (!string.IsNullOrEmpty(file))
            {
                var psi = new ProcessStartInfo(file)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using var _ = Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[LauncherView] LaunchAsAdmin failed: {ex.Message}");
        }
    }

    private static void OpenLocation(LauncherDisplayItem item)
    {
        try
        {
            string? file = ResolveExePath(item.TargetPath);
            if (!string.IsNullOrEmpty(file) && File.Exists(file))
            {
                using var _ = Process.Start("explorer.exe", $"/select,\"{file}\"");
            }
            else if (Directory.Exists(item.TargetPath))
            {
                using var _ = Process.Start("explorer.exe", $"\"{item.TargetPath}\"");
            }
        }
        catch { }
    }

    private void UnpinItem(LauncherDisplayItem item)
    {
        if (Island?.QuickLauncherServiceInstance?.Remove(item.Id) == true)
        {
            RefreshItems();
            Island?.PushNotificationToast("\uE74D", "Quick Launcher", $"Unpinned {item.Name}", ThemeBrushes.GreenColor);
        }
    }

    private void AppCard_Drop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LauncherDisplayItem item })
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                bool ok = Island?.QuickLauncherServiceInstance?.Launch(item.Id, files) ?? false;
                Island?.PushNotificationToast(item.Glyph, $"Opened in {item.Name}", $"{files.Length} file(s) sent", ThemeBrushes.GreenColor);
                RefreshItems();
                e.Handled = true;
            }
        }
    }

    private void AppCard_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void LauncherView_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            int added = 0;
            foreach (var file in files)
            {
                if (File.Exists(file) || Directory.Exists(file))
                {
                    Island?.QuickLauncherServiceInstance?.AddTarget(file);
                    added++;
                }
            }
            if (added > 0)
            {
                RefreshItems();
                LauncherScrollViewer?.ScrollToTop();
                Island?.PushNotificationToast("\uE710", "Quick Launcher", $"Pinned {added} item(s)", ThemeBrushes.GreenColor);
            }
            e.Handled = true;
        }
    }

    private void LauncherView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    public void Scroll(double delta)
    {
        if (LauncherScrollViewer != null)
        {
            double target = Math.Clamp(LauncherScrollViewer.VerticalOffset + delta, 0, Math.Max(0, LauncherScrollViewer.ScrollableHeight));
            LauncherScrollViewer.ScrollToVerticalOffset(target);
        }
    }

    private void LauncherView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (LauncherScrollViewer != null)
        {
            double step = e.Delta > 0 ? -64.0 : 64.0;
            double target = Math.Clamp(LauncherScrollViewer.VerticalOffset + step, 0, Math.Max(0, LauncherScrollViewer.ScrollableHeight));
            LauncherScrollViewer.ScrollToVerticalOffset(target);
            e.Handled = true;
        }
    }

    private void AddCustomAppBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Application or Shortcut to Pin",
                Filter = "Applications & Shortcuts (*.exe;*.lnk;*.bat;*.cmd;*.ps1)|*.exe;*.lnk;*.bat;*.cmd;*.ps1|All Files (*.*)|*.*",
                CheckFileExists = true,
                DereferenceLinks = true,
                Multiselect = false
            };

            var owner = Island ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            bool? result;
            if (owner != null)
            {
                owner.Activate();
                result = dlg.ShowDialog(owner);
            }
            else
            {
                result = dlg.ShowDialog();
            }

            if (result == true && !string.IsNullOrWhiteSpace(dlg.FileName))
            {
                var svc = Island?.QuickLauncherServiceInstance;
                if (svc != null)
                {
                    svc.AddTarget(dlg.FileName);
                    RefreshItems();
                    LauncherScrollViewer?.ScrollToTop();
                    Island?.PushNotificationToast("\uE710", "Quick Launcher", $"Pinned {Path.GetFileNameWithoutExtension(dlg.FileName)}", ThemeBrushes.GreenColor);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[LauncherView] Pin app error: {ex.Message}");
        }
    }
}
