using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    private static readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private enum LauncherFilterMode { All, Pinned, Widgets }
    private LauncherFilterMode _currentFilter = LauncherFilterMode.All;
    private string _searchQuery = "";

    private List<LauncherDisplayItem> _allCombined = new();
    private List<LauncherDisplayItem> _pinnedOnly = new();
    private List<LauncherDisplayItem> _widgetsOnly = new();

    #region Win32 Shell Icon Extraction Interop
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
    #endregion

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
        _currentFilter = LauncherFilterMode.Widgets;
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
            LauncherFilterMode.Pinned => _pinnedOnly,
            LauncherFilterMode.Widgets => _widgetsOnly,
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
        EmptyAppsPrompt.Visibility = (!hasSearch && _currentFilter == LauncherFilterMode.Pinned && _pinnedOnly.Count == 0)
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

        TabWidgetsBtn.Background = _currentFilter == LauncherFilterMode.Widgets ? TabActiveBg : TabInactiveBg;
        TabWidgetsBtn.Foreground = _currentFilter == LauncherFilterMode.Widgets ? TabActiveFg : TabInactiveFg;
    }

    public static ImageSource? GetAppIcon(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        lock (_iconCache)
        {
            if (_iconCache.TryGetValue(targetPath, out var cached))
                return cached;
        }

        ImageSource? result = null;
        try
        {
            string? resolved = ResolveExePath(targetPath);
            if (!string.IsNullOrEmpty(resolved) && (File.Exists(resolved) || Directory.Exists(resolved)))
            {
                var shinfo = new SHFILEINFO();
                IntPtr hSuccess = SHGetFileInfo(resolved, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
                if (hSuccess != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        result = bmp;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
        }
        catch
        {
            result = null;
        }

        lock (_iconCache)
        {
            _iconCache[targetPath] = result;
        }

        return result;
    }

    private static string? ResolveExePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (File.Exists(path) || Directory.Exists(path)) return path;

            // Expand environment variables (e.g. %SystemRoot%\System32\cmd.exe)
            string expanded = Environment.ExpandEnvironmentVariables(path);
            if (File.Exists(expanded)) return expanded;

            // System32 lookup
            string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string cand = Path.Combine(system32, path);
            if (File.Exists(cand)) return cand;
            if (!cand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(cand + ".exe"))
                return cand + ".exe";

            // Windows directory lookup
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            cand = Path.Combine(windir, path);
            if (File.Exists(cand)) return cand;
            if (!cand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(cand + ".exe"))
                return cand + ".exe";

            // PATH environment resolution
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    try
                    {
                        cand = Path.Combine(dir, path);
                        if (File.Exists(cand)) return cand;
                        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            cand = Path.Combine(dir, path + ".exe");
                            if (File.Exists(cand)) return cand;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return null;
    }

    public void RefreshItems()
    {
        var builtInWidgets = new List<LauncherDisplayItem>
        {
            new() { Id = "island:calc", Name = "Calculator", Subtitle = "Math & Units", Glyph = "\uE8EF", TargetPath = "island:calc", AccentBrush = GreenAccent, IconBgBrush = GreenBg, IconBorderBrush = GreenBorder, FriendlyDescription = "Fast arithmetic, percentages & unit conversions", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:converter", Name = "File Converter", Subtitle = "PDF & Images", Glyph = "\uEA90", TargetPath = "island:converter", AccentBrush = BlueAccent, IconBgBrush = BlueBg, IconBorderBrush = BlueBorder, FriendlyDescription = "Batch convert PNG, JPG, WebP & merge PDFs", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:notes", Name = "Quick Notes", Subtitle = "Scratchpad", Glyph = "\uE70B", TargetPath = "island:notes", AccentBrush = YellowAccent, IconBgBrush = YellowBg, IconBorderBrush = YellowBorder, FriendlyDescription = "Markdown scratchpad, pinned checklists & notes", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:termi", Name = "Termi-Notch", Subtitle = "Console & Scripts", Glyph = "\uE756", TargetPath = "island:termi", AccentBrush = PurpleAccent, IconBgBrush = PurpleBg, IconBorderBrush = PurpleBorder, FriendlyDescription = "Command runner & quick terminal snippets", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:network", Name = "Wi-Fi Hub", Subtitle = "Speed & Networks", Glyph = "\uE701", TargetPath = "island:network", AccentBrush = BlueAccent, IconBgBrush = BlueBg, IconBorderBrush = BlueBorder, FriendlyDescription = "Live bandwidth, Wi-Fi toggles & network diagnostics", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:systemhud", Name = "System HUD", Subtitle = "CPU, RAM & Vitals", Glyph = "\uE9D9", TargetPath = "island:systemhud", AccentBrush = OrangeAccent, IconBgBrush = OrangeBg, IconBorderBrush = OrangeBorder, FriendlyDescription = "Hardware monitor, CPU/RAM delta & top processes", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:phonebridge", Name = "Phone Bridge", Subtitle = "LAN QuickShare", Glyph = "\uE8EA", TargetPath = "island:phonebridge", AccentBrush = GreenAccent, IconBgBrush = GreenBg, IconBorderBrush = GreenBorder, FriendlyDescription = "Wireless LAN file transfer & clipboard bridge", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:capturestudio", Name = "Capture Studio", Subtitle = "OCR & Snip", Glyph = "\uE924", TargetPath = "island:capturestudio", AccentBrush = CyanAccent, IconBgBrush = CyanBg, IconBorderBrush = CyanBorder, FriendlyDescription = "Screen capture, OCR text extraction & pin overlays", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:mechsounds", Name = "Mech Sounds", Subtitle = "Tactile Studio", Glyph = "\uE765", TargetPath = "island:mechsounds", AccentBrush = PinkAccent, IconBgBrush = PinkBg, IconBorderBrush = PinkBorder, FriendlyDescription = "Mechanical keyboard audio feedback & volume studio", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:battery", Name = "Battery & Power", Subtitle = "Energy Profiles", Glyph = "\uE945", TargetPath = "island:battery", AccentBrush = GreenAccent, IconBgBrush = GreenBg, IconBorderBrush = GreenBorder, FriendlyDescription = "Power plans, health telemetry & battery runtime", ActionHint = "Click to open · Right-click for options" },
            new() { Id = "island:voicememo", Name = "Voice Memos", Subtitle = "Audio Recorder", Glyph = "\uE7C8", TargetPath = "island:voicememo", AccentBrush = RedAccent, IconBgBrush = RedBg, IconBorderBrush = RedBorder, FriendlyDescription = "High quality microphone audio recording & memos", ActionHint = "Click to open · Right-click for options" },
        };

        _widgetsOnly = builtInWidgets;

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

        // Unified layout: Pinned apps seamlessly flow first, followed directly by island widgets
        _allCombined = _pinnedOnly.Concat(_widgetsOnly).ToList();

        TabAllBtn.Content = $"All ({_allCombined.Count})";
        TabPinnedBtn.Content = $"Pinned ({_pinnedOnly.Count})";
        TabWidgetsBtn.Content = $"Widgets ({_widgetsOnly.Count})";

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
                Process.Start(psi);
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
                Process.Start("explorer.exe", $"/select,\"{file}\"");
            }
            else if (Directory.Exists(item.TargetPath))
            {
                Process.Start("explorer.exe", $"\"{item.TargetPath}\"");
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