using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Options object aggregating callbacks and state predicates for <see cref="TrayIconService"/>.
/// </summary>
internal sealed class TrayIconOptions
{
    public required Action OnExpand { get; init; }
    public required Action OnAudioSwitch { get; init; }
    public required Action OnPower { get; init; }
    public required Action OnVoiceMemo { get; init; }
    public required Action OnPeripherals { get; init; }
    public required Action OnSettings { get; init; }
    public required Action OnTestToast { get; init; }
    public required Action OnToggleStopwatch { get; init; }
    public required Action OnExit { get; init; }
    public Action? OnSystemHud { get; init; }
    public Action? OnShelf { get; init; }
    public Action? OnClipboard { get; init; }
    public Action? OnNotes { get; init; }
    public Action? OnCalc { get; init; }
    public Action? OnMirror { get; init; }
    public Func<bool>? IsCaffeineEnabled { get; init; }
    public Action<bool>? SetCaffeineEnabled { get; init; }
    public Func<bool>? IsDndEnabled { get; init; }
    public Action<bool>? SetDndEnabled { get; init; }
    public Func<bool>? IsReplaceBannersEnabled { get; init; }
    public Action<bool>? SetReplaceBannersEnabled { get; init; }
    public Func<bool>? IsNetworkSpeedEnabled { get; init; }
    public Action<bool>? SetNetworkSpeedEnabled { get; init; }
    public Func<string>? GetThemeMode { get; init; }
    public Action<string>? SetThemeMode { get; init; }
    public Func<bool>? IsEdgeBorderEnabled { get; init; }
    public Action<bool>? SetEdgeBorderEnabled { get; init; }
    public Func<string>? GetThemeStyle { get; init; }
    public Action<string>? SetThemeStyle { get; init; }
    public Func<string>? GetIslandTheme { get; init; }
    public Action<string>? SetIslandTheme { get; init; }
    public Action? OnCommandPalette { get; init; }
    public Action? OnExportDiagnostics { get; init; }
    public Func<IReadOnlyList<IslandProfile>>? GetProfiles { get; init; }
    public Func<string>? GetActiveProfileId { get; init; }
    public Action<string>? OnSelectProfile { get; init; }
}

/// <summary>
/// Native Shell_NotifyIcon system tray integration with a dark-mode WPF context menu.
/// Avoids Windows Forms dependencies and gives full WPF control over tray events.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    public const int WM_TRAYICON = 0x8001; // WM_APP + 1

    private readonly IntPtr _hwnd;
    private readonly Action _onExpand;
    private readonly Action _onAudioSwitch;
    private readonly Action _onPower;
    private readonly Action? _onSystemHud;
    private readonly Action _onVoiceMemo;
    private readonly Action _onPeripherals;
    private readonly Action _onSettings;
    private readonly Action _onTestToast;
    private readonly Action _onToggleStopwatch;
    private readonly Action _onExit;
    private readonly Action? _onShelf;
    private readonly Action? _onClipboard;
    private readonly Action? _onNotes;
    private readonly Action? _onCalc;
    private readonly Action? _onMirror;
    private readonly Func<bool>? _isCaffeineEnabled;
    private readonly Action<bool>? _setCaffeineEnabled;
    private readonly Func<bool>? _isDndEnabled;
    private readonly Action<bool>? _setDndEnabled;
    private readonly Func<bool>? _isReplaceBannersEnabled;
    private readonly Action<bool>? _setReplaceBannersEnabled;
    private readonly Func<bool>? _isNetworkSpeedEnabled;
    private readonly Action<bool>? _setNetworkSpeedEnabled;
    private readonly Func<string>? _getThemeMode;
    private readonly Action<string>? _setThemeMode;
    private readonly Func<bool>? _isEdgeBorderEnabled;
    private readonly Action<bool>? _setEdgeBorderEnabled;
    private readonly Func<string>? _getThemeStyle;
    private readonly Action<string>? _setThemeStyle;
    private readonly Func<string>? _getIslandTheme;
    private readonly Action<string>? _setIslandTheme;
    private readonly Action? _onCommandPalette;
    private readonly Action? _onExportDiagnostics;
    private readonly Func<IReadOnlyList<IslandProfile>>? _getProfiles;
    private readonly Func<string>? _getActiveProfileId;
    private readonly Action<string>? _onSelectProfile;
    private IntPtr _hIcon;
    private bool _added;

    public TrayIconService(IntPtr hwnd, TrayIconOptions options)
        : this(
            hwnd,
            (options ?? throw new ArgumentNullException(nameof(options))).OnExpand,
            options.OnAudioSwitch,
            options.OnPower,
            options.OnVoiceMemo,
            options.OnPeripherals,
            options.OnSettings,
            options.OnTestToast,
            options.OnToggleStopwatch,
            options.OnExit,
            options.OnSystemHud,
            options.OnShelf,
            options.OnClipboard,
            options.OnNotes,
            options.OnCalc,
            options.OnMirror,
            options.IsCaffeineEnabled,
            options.SetCaffeineEnabled,
            options.IsDndEnabled,
            options.SetDndEnabled,
            options.IsReplaceBannersEnabled,
            options.SetReplaceBannersEnabled,
            options.IsNetworkSpeedEnabled,
            options.SetNetworkSpeedEnabled,
            options.GetThemeMode,
            options.SetThemeMode,
            options.IsEdgeBorderEnabled,
            options.SetEdgeBorderEnabled,
            options.GetThemeStyle,
            options.SetThemeStyle,
            options.GetIslandTheme,
            options.SetIslandTheme)
    {
        _onCommandPalette = options.OnCommandPalette;
        _onExportDiagnostics = options.OnExportDiagnostics;
        _getProfiles = options.GetProfiles;
        _getActiveProfileId = options.GetActiveProfileId;
        _onSelectProfile = options.OnSelectProfile;
    }

    public TrayIconService(
        IntPtr hwnd,
        Action onExpand,
        Action onAudioSwitch,
        Action onPower,
        Action onVoiceMemo,
        Action onPeripherals,
        Action onSettings,
        Action onTestToast,
        Action onToggleStopwatch,
        Action onExit,
        Action? onSystemHud = null,
        Action? onShelf = null,
        Action? onClipboard = null,
        Action? onNotes = null,
        Action? onCalc = null,
        Action? onMirror = null,
        Func<bool>? isCaffeineEnabled = null,
        Action<bool>? setCaffeineEnabled = null,
        Func<bool>? isDndEnabled = null,
        Action<bool>? setDndEnabled = null,
        Func<bool>? isReplaceBannersEnabled = null,
        Action<bool>? setReplaceBannersEnabled = null,
        Func<bool>? isNetworkSpeedEnabled = null,
        Action<bool>? setNetworkSpeedEnabled = null,
        Func<string>? getThemeMode = null,
        Action<string>? setThemeMode = null,
        Func<bool>? isEdgeBorderEnabled = null,
        Action<bool>? setEdgeBorderEnabled = null,
        Func<string>? getThemeStyle = null,
        Action<string>? setThemeStyle = null,
        Func<string>? getIslandTheme = null,
        Action<string>? setIslandTheme = null)
    {
        _hwnd = hwnd;
        _onExpand = onExpand;
        _onAudioSwitch = onAudioSwitch;
        _onPower = onPower;
        _onSystemHud = onSystemHud;
        _onVoiceMemo = onVoiceMemo;
        _onPeripherals = onPeripherals;
        _onSettings = onSettings;
        _onTestToast = onTestToast;
        _onToggleStopwatch = onToggleStopwatch;
        _onExit = onExit;
        _onShelf = onShelf;
        _onClipboard = onClipboard;
        _onNotes = onNotes;
        _onCalc = onCalc;
        _onMirror = onMirror;
        _isCaffeineEnabled = isCaffeineEnabled;
        _setCaffeineEnabled = setCaffeineEnabled;
        _isDndEnabled = isDndEnabled;
        _setDndEnabled = setDndEnabled;
        _isReplaceBannersEnabled = isReplaceBannersEnabled;
        _setReplaceBannersEnabled = setReplaceBannersEnabled;
        _isNetworkSpeedEnabled = isNetworkSpeedEnabled;
        _setNetworkSpeedEnabled = setNetworkSpeedEnabled;
        _getThemeMode = getThemeMode;
        _setThemeMode = setThemeMode;
        _isEdgeBorderEnabled = isEdgeBorderEnabled;
        _setEdgeBorderEnabled = setEdgeBorderEnabled;
        _getThemeStyle = getThemeStyle;
        _setThemeStyle = setThemeStyle;
        _getIslandTheme = getIslandTheme;
        _setIslandTheme = setIslandTheme;

        string? exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            _hIcon = ExtractIconW(IntPtr.Zero, exe, 0);

        if (_hIcon == IntPtr.Zero || _hIcon == new IntPtr(1))
        {
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icoPath))
            {
                _hIcon = ExtractIconW(IntPtr.Zero, icoPath, 0);
            }
        }

        if (_hIcon == IntPtr.Zero || _hIcon == new IntPtr(1))
            _hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "Dynamic Island · by Jainis Patel"
        };

        _added = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    public void HandleMessage(IntPtr lParam)
    {
        int msg = (int)lParam.ToInt64() & 0xFFFF;
        switch (msg)
        {
            case WM_LBUTTONUP:
                _onExpand();
                break;
            case WM_LBUTTONDBLCLK:
                _onSettings();
                break;
            case WM_RBUTTONUP:
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        SetForegroundWindow(_hwnd);

        var menu = new ContextMenu();

        static MenuItem CreateItem(string header, string? glyph = null, System.Windows.Media.Brush? iconBrush = null) => new()
        {
            Header = header,
            Icon = glyph != null ? ThemeBrushes.CreateMenuIcon(glyph, iconBrush) : null
        };

        var itemBrand = new MenuItem
        {
            Header = "Dynamic Island · by Jainis Patel",
            Icon = ThemeBrushes.CreateMenuIcon("\uE7E7", ThemeBrushes.Accent),
            IsEnabled = false,
            Foreground = ThemeBrushes.MutedGrey,
            FontSize = 10
        };
        menu.Items.Add(itemBrand);
        menu.Items.Add(new Separator());

        var itemExpand = CreateItem("Expand Dynamic Island", "\uE70D", ThemeBrushes.Accent);
        itemExpand.FontWeight = FontWeights.SemiBold;
        itemExpand.Click += (_, _) => _onExpand();
        menu.Items.Add(itemExpand);

        if (_onCommandPalette != null)
        {
            var itemPalette = CreateItem("Command Palette... (Ctrl+Alt+G)", "\uE721", ThemeBrushes.Accent);
            itemPalette.Click += (_, _) => _onCommandPalette();
            menu.Items.Add(itemPalette);
        }

        if (_onShelf != null)
        {
            var itemShelf = CreateItem("Drop Shelf...", "\uE8F4");
            itemShelf.Click += (_, _) => _onShelf();
            menu.Items.Add(itemShelf);
        }

        if (_onClipboard != null)
        {
            var itemClip = CreateItem("Clipboard History... (Ctrl+Alt+V)", "\uE77F");
            itemClip.Click += (_, _) => _onClipboard();
            menu.Items.Add(itemClip);
        }

        if (_onCalc != null)
        {
            var itemCalc = CreateItem("Calculator & Converter...", "\uE8EF");
            itemCalc.Click += (_, _) => _onCalc();
            menu.Items.Add(itemCalc);
        }

        if (_onMirror != null)
        {
            var itemMirror = CreateItem("Webcam Mirror & Mic Check...", "\uE714");
            itemMirror.Click += (_, _) => _onMirror();
            menu.Items.Add(itemMirror);
        }

        var itemAudio = CreateItem("Audio Output & Bluetooth... (Ctrl+Alt+O)", "\uE7F6");
        itemAudio.Click += (_, _) => _onAudioSwitch();
        menu.Items.Add(itemAudio);

        var itemPower = CreateItem("Power & Battery Widget... (Ctrl+Alt+P)", "\uEBA0");
        itemPower.Click += (_, _) => _onPower();
        menu.Items.Add(itemPower);

        if (_onSystemHud != null)
        {
            var itemHud = CreateItem("Activity & System HUD... (Ctrl+Alt+H)", "\uE9D9");
            itemHud.Click += (_, _) => _onSystemHud();
            menu.Items.Add(itemHud);
        }

        var itemVoice = CreateItem("Voice Memo & Dictation... (Ctrl+Alt+R)", "\uE720");
        itemVoice.Click += (_, _) => _onVoiceMemo();
        menu.Items.Add(itemVoice);

        var itemPeripherals = CreateItem("Peripheral Battery Hub... (Ctrl+Alt+D)", "\uE702");
        itemPeripherals.Click += (_, _) => _onPeripherals();
        menu.Items.Add(itemPeripherals);

        if (_onNotes != null)
        {
            var itemNotes = CreateItem("Quick Notes & Scratchpad... (Ctrl+Alt+J)", "\uE70B");
            itemNotes.Click += (_, _) => _onNotes();
            menu.Items.Add(itemNotes);
        }

        var itemSettings = CreateItem("Customize & Notch Settings...", "\uE713");
        itemSettings.Click += (_, _) => _onSettings();
        menu.Items.Add(itemSettings);

        var itemStopwatch = CreateItem("Toggle Stopwatch (Ctrl+Alt+A)", "\uE916");
        itemStopwatch.Click += (_, _) => _onToggleStopwatch();
        menu.Items.Add(itemStopwatch);

        menu.Items.Add(new Separator());

        if (_isCaffeineEnabled != null && _setCaffeineEnabled != null)
        {
            var itemCaffeine = CreateItem("Keep Awake / Caffeine Mode (Prevent Sleep)", "\uEC32");
            itemCaffeine.IsCheckable = true;
            itemCaffeine.IsChecked = _isCaffeineEnabled();
            itemCaffeine.Click += (_, _) => _setCaffeineEnabled(itemCaffeine.IsChecked);
            menu.Items.Add(itemCaffeine);
        }

        if (_isDndEnabled != null && _setDndEnabled != null)
        {
            var itemDnd = CreateItem("Do Not Disturb (Silence Notification Toasts)", "\uE708");
            itemDnd.IsCheckable = true;
            itemDnd.IsChecked = _isDndEnabled();
            itemDnd.Click += (_, _) => _setDndEnabled(itemDnd.IsChecked);
            menu.Items.Add(itemDnd);
        }

        if (_isReplaceBannersEnabled != null && _setReplaceBannersEnabled != null)
        {
            var itemReplace = CreateItem("Show Notifications in Island Only (Hide Windows Banners)", "\uE7E7");
            itemReplace.IsCheckable = true;
            itemReplace.IsChecked = _isReplaceBannersEnabled();
            itemReplace.Click += (_, _) => _setReplaceBannersEnabled(itemReplace.IsChecked);
            menu.Items.Add(itemReplace);
        }

        if (_isNetworkSpeedEnabled != null && _setNetworkSpeedEnabled != null)
        {
            var itemNetSpeed = CreateItem("Show Internet Speed in Pill", "\uE774");
            itemNetSpeed.IsCheckable = true;
            itemNetSpeed.IsChecked = _isNetworkSpeedEnabled();
            itemNetSpeed.Click += (_, _) => _setNetworkSpeedEnabled(itemNetSpeed.IsChecked);
            menu.Items.Add(itemNetSpeed);
        }

        if (_isEdgeBorderEnabled != null && _setEdgeBorderEnabled != null)
        {
            var itemBorder = CreateItem("Island Edge Border", "\uE73A");
            itemBorder.IsCheckable = true;
            itemBorder.IsChecked = _isEdgeBorderEnabled();
            itemBorder.Click += (_, _) => _setEdgeBorderEnabled(itemBorder.IsChecked);
            menu.Items.Add(itemBorder);
        }

        var itemStartup = CreateItem("Run at Windows Startup", "\uE7E8");
        itemStartup.IsCheckable = true;
        itemStartup.IsChecked = StartupService.IsStartupEnabled();
        itemStartup.Click += (_, _) => StartupService.SetStartup(itemStartup.IsChecked);
        menu.Items.Add(itemStartup);

        if (_getThemeMode != null && _setThemeMode != null)
        {
            var menuTheme = CreateItem("Appearance Mode", "\uE706");
            string mode = SettingsService.NormalizeThemeMode(_getThemeMode());

            var itemSystem = new MenuItem
            {
                Header = $"System (Auto: {(ThemeManager.IsLight ? "Light" : "Dark")})",
                IsCheckable = true,
                IsChecked = mode == "System"
            };
            itemSystem.Click += (_, _) => _setThemeMode("System");

            var itemDark = new MenuItem
            {
                Header = "Dark Mode",
                IsCheckable = true,
                IsChecked = mode == "Dark"
            };
            itemDark.Click += (_, _) => _setThemeMode("Dark");

            var itemLight = new MenuItem
            {
                Header = "Light Mode",
                IsCheckable = true,
                IsChecked = mode == "Light"
            };
            itemLight.Click += (_, _) => _setThemeMode("Light");

            menuTheme.Items.Add(itemSystem);
            menuTheme.Items.Add(itemDark);
            menuTheme.Items.Add(itemLight);
            menu.Items.Add(menuTheme);
        }

        if (_getThemeStyle != null && _setThemeStyle != null)
        {
            var menuStyle = CreateItem("UI Theme", "\uE790");
            string style = _getThemeStyle();
            bool isLiquid = string.Equals(style, "Liquid Glass", StringComparison.OrdinalIgnoreCase);

            var itemDefault = new MenuItem
            {
                Header = "Default (Classic OLED)",
                IsCheckable = true,
                IsChecked = !isLiquid
            };
            itemDefault.Click += (_, _) => _setThemeStyle("Default");

            var itemLiquid = new MenuItem
            {
                Header = "Liquid Glass (macOS)",
                IsCheckable = true,
                IsChecked = isLiquid
            };
            itemLiquid.Click += (_, _) => _setThemeStyle("Liquid Glass");

            menuStyle.Items.Add(itemDefault);
            menuStyle.Items.Add(itemLiquid);
            menu.Items.Add(menuStyle);
        }

        if (_getIslandTheme != null && _setIslandTheme != null)
        {
            var menuNature = CreateItem("Living Island Theme", "\uEC0A");
            string currentTheme = SettingsService.NormalizeIslandTheme(_getIslandTheme());

            foreach (var t in SettingsService.AvailableIslandThemes)
            {
                var item = new MenuItem
                {
                    Header = t,
                    IsCheckable = true,
                    IsChecked = string.Equals(currentTheme, t, StringComparison.OrdinalIgnoreCase)
                };
                string themeVal = t;
                item.Click += (_, _) => _setIslandTheme(themeVal);
                menuNature.Items.Add(item);
            }
            menu.Items.Add(menuNature);
        }

        if (_getProfiles != null && _onSelectProfile != null)
        {
            var menuProfiles = CreateItem("Context Profile", "\uE734");
            string activeId = _getActiveProfileId?.Invoke() ?? "default";
            foreach (var prof in _getProfiles())
            {
                var pItem = new MenuItem
                {
                    Header = prof.Name,
                    IsCheckable = true,
                    IsChecked = string.Equals(prof.Id, activeId, StringComparison.OrdinalIgnoreCase)
                };
                string profId = prof.Id;
                pItem.Click += (_, _) => _onSelectProfile(profId);
                menuProfiles.Items.Add(pItem);
            }
            menu.Items.Add(menuProfiles);
        }

        if (_onExportDiagnostics != null)
        {
            var itemDiag = CreateItem("Export Diagnostics Bundle", "\uE9D9");
            itemDiag.Click += (_, _) => _onExportDiagnostics();
            menu.Items.Add(itemDiag);
        }

#if DEBUG
        var itemToast = CreateItem("Send Test Alert (Ctrl+Alt+T)", "\uEA8F");
        itemToast.Click += (_, _) => _onTestToast();
        menu.Items.Add(itemToast);
#endif

        menu.Items.Add(new Separator());

        var itemExit = CreateItem("Exit Dynamic Island (Ctrl+Alt+Q)", "\uE7E8", ThemeBrushes.Red);
        itemExit.Click += (_, _) => _onExit();
        menu.Items.Add(itemExit);

        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_added)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _added = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); } catch { }
            _hIcon = IntPtr.Zero;
        }
    }
}
