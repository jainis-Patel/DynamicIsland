using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

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
    private readonly Action _onVoiceMemo;
    private readonly Action _onPeripherals;
    private readonly Action _onSettings;
    private readonly Action _onTestToast;
    private readonly Action _onToggleStopwatch;
    private readonly Action _onExit;
    private readonly Func<bool>? _isDndEnabled;
    private readonly Action<bool>? _setDndEnabled;
    private IntPtr _hIcon;
    private bool _added;

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
        Func<bool>? isDndEnabled = null,
        Action<bool>? setDndEnabled = null)
    {
        _hwnd = hwnd;
        _onExpand = onExpand;
        _onAudioSwitch = onAudioSwitch;
        _onPower = onPower;
        _onVoiceMemo = onVoiceMemo;
        _onPeripherals = onPeripherals;
        _onSettings = onSettings;
        _onTestToast = onTestToast;
        _onToggleStopwatch = onToggleStopwatch;
        _onExit = onExit;
        _isDndEnabled = isDndEnabled;
        _setDndEnabled = setDndEnabled;

        string? exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            _hIcon = ExtractIconW(IntPtr.Zero, exe, 0);

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
            szTip = "Dynamic Island (Top-Center Desktop Pill)"
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

        var menu = new ContextMenu
        {
            Background = ThemeBrushes.MenuBackground,
            Foreground = ThemeBrushes.PrimaryText,
            BorderBrush = ThemeBrushes.MenuBorder,
            BorderThickness = new Thickness(1),
            FontSize = 12.5
        };

        var itemExpand = new MenuItem { Header = "Expand Dynamic Island", FontWeight = FontWeights.SemiBold };
        itemExpand.Click += (_, _) => _onExpand();
        menu.Items.Add(itemExpand);

        var itemAudio = new MenuItem { Header = "Audio Output & Bluetooth... (Ctrl+Alt+O)" };
        itemAudio.Click += (_, _) => _onAudioSwitch();
        menu.Items.Add(itemAudio);

        var itemPower = new MenuItem { Header = "Power & Battery Widget... (Ctrl+Alt+P)" };
        itemPower.Click += (_, _) => _onPower();
        menu.Items.Add(itemPower);

        var itemVoice = new MenuItem { Header = "Voice Memo & Dictation... (Ctrl+Alt+R)" };
        itemVoice.Click += (_, _) => _onVoiceMemo();
        menu.Items.Add(itemVoice);

        var itemPeripherals = new MenuItem { Header = "Peripheral Battery Hub... (Ctrl+Alt+D)" };
        itemPeripherals.Click += (_, _) => _onPeripherals();
        menu.Items.Add(itemPeripherals);

        var itemSettings = new MenuItem { Header = "Customize & Notch Settings..." };
        itemSettings.Click += (_, _) => _onSettings();
        menu.Items.Add(itemSettings);

        var itemStopwatch = new MenuItem { Header = "Toggle Stopwatch (Ctrl+Alt+A)" };
        itemStopwatch.Click += (_, _) => _onToggleStopwatch();
        menu.Items.Add(itemStopwatch);

        menu.Items.Add(new Separator { Background = ThemeBrushes.MenuSeparator });

        if (_isDndEnabled != null && _setDndEnabled != null)
        {
            var itemDnd = new MenuItem
            {
                Header = "Do Not Disturb (Silence Notification Toasts)",
                IsCheckable = true,
                IsChecked = _isDndEnabled()
            };
            itemDnd.Click += (_, _) => _setDndEnabled(itemDnd.IsChecked);
            menu.Items.Add(itemDnd);
        }

        var itemStartup = new MenuItem
        {
            Header = "Run at Windows Startup",
            IsCheckable = true,
            IsChecked = StartupService.IsStartupEnabled()
        };
        itemStartup.Click += (_, _) => StartupService.SetStartup(itemStartup.IsChecked);
        menu.Items.Add(itemStartup);

#if DEBUG
        var itemToast = new MenuItem { Header = "Send Test Alert (Ctrl+Alt+T)" };
        itemToast.Click += (_, _) => _onTestToast();
        menu.Items.Add(itemToast);
#endif

        menu.Items.Add(new Separator { Background = ThemeBrushes.MenuSeparator });

        var itemExit = new MenuItem { Header = "Exit Dynamic Island (Ctrl+Alt+Q)" };
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
