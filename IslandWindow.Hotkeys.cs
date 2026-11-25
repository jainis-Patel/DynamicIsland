using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    private const int HotkeyStopwatch = 1;
    private const int HotkeyToast = 2;
    private const int HotkeyQuit = 3;
    private const int HotkeyMedia = 4;
    private const int HotkeyClipboard = 5;
    private const int HotkeyCalc = 6;
    private const int HotkeyAudio = 7;
    private const int HotkeyPower = 8;
    private const int HotkeyRecord = 9;
    private const int HotkeyPeripherals = 10;
    private const int HotkeyNetwork = 11;
    private const int HotkeyNotifications = 12;
    private const int HotkeySettings = 13;
    private const int HotkeyMonitor = 14;
    private const int HotkeySystemHud = 15;
    private const int HotkeyQuickPaste = 16;
    private const int HotkeyCapture = 17;
    private const int HotkeyNotes = 18;
    private const int HotkeyTuck = 19;
    private const int HotkeyDock = 20;
    private const int HotkeyMicMute = 21;
    private const int HotkeyPromptApprove = 22;
    private const int HotkeyPromptReject = 23;
    private const int HotkeyToggleFloat = 24;

    private static readonly (int Id, string KeyName, uint DefaultVk)[] HotKeyBindings =
    {
        (HotkeyStopwatch, "Stopwatch", 0x41),         // VK_A
        (HotkeyToast, "TestToast", 0x54),             // VK_T
        (HotkeyQuit, "Quit", 0x51),                   // VK_Q
        (HotkeyMedia, "MediaPlayPause", 0x20),        // VK_SPACE
        (HotkeyClipboard, "Clipboard", 0x56),         // VK_V
        (HotkeyCalc, "Calculator", 0x43),             // VK_C
        (HotkeyAudio, "AudioDevices", 0x4F),          // VK_O
        (HotkeyPower, "BatteryPower", 0x50),          // VK_P
        (HotkeyRecord, "VoiceMemo", 0x52),            // VK_R
        (HotkeyPeripherals, "Peripherals", 0x44),     // VK_D
        (HotkeyNetwork, "Network", 0x57),             // VK_W
        (HotkeyNotifications, "Notifications", 0x4E), // VK_N
        (HotkeySettings, "Settings", 0x53),           // VK_S
        (HotkeyMonitor, "SwitchMonitor", 0x4D),       // VK_M (Ctrl+Alt+M)
        (HotkeySystemHud, "SystemHud", 0x48),         // VK_H (Ctrl+Alt+H)
        (HotkeyQuickPaste, "QuickPaste", 0x42),       // VK_B (Ctrl+Alt+B)
        (HotkeyCapture, "ElementCapture", 0x58),      // VK_X (Ctrl+Alt+X)
        (HotkeyNotes, "QuickNotes", 0x4A),           // VK_J (Ctrl+Alt+J)
        (HotkeyTuck, "TuckIsland", 0x28),             // VK_DOWN (Ctrl+Alt+Down)
        (HotkeyDock, "CycleDock", 0x5A),              // VK_Z (Ctrl+Alt+Z)
        (HotkeyMicMute, "MicrophoneMute", 0x4B),      // VK_K (Ctrl+Alt+K)
        (HotkeyPromptApprove, "PromptApprove", 0x59), // VK_Y (Ctrl+Alt+Y)
        (HotkeyPromptReject, "PromptReject", 0x55),   // VK_U (Ctrl+Alt+U)
        (HotkeyToggleFloat, "ToggleFloating", 0x46),  // VK_F (Ctrl+Alt+F)
    };

    private readonly Dictionary<string, bool> _hotkeyRegistrationStatus = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _llKeyboardHook;
    private LowLevelKeyboardProc? _llKeyboardProc;
    public IReadOnlyDictionary<string, bool> HotkeyRegistrationStatus => _hotkeyRegistrationStatus;
    private void RegisterConfiguredHotKeys()
    {
        if (_hwnd == IntPtr.Zero) return;
        UnregisterAllHotKeys();

        bool allowDevHotkeys = _settingsService?.Current.EnableDeveloperHotkeys ?? false;
#if DEBUG
        allowDevHotkeys = true;
#endif

        var custom = _settingsService?.Current.CustomHotkeys;
        foreach (var (id, keyName, defaultVk) in HotKeyBindings)
        {
            if (id == HotkeyToast && !allowDevHotkeys)
            {
                continue;
            }

            uint vk = defaultVk;
            if (custom != null && custom.TryGetValue(keyName, out uint customVk))
            {
                vk = customVk;
            }

            // Setting a custom VK of 0 disables that hotkey
            if (vk == 0)
            {
                _hotkeyRegistrationStatus[keyName] = false;
                continue;
            }

            bool ok = RegisterHotKey(_hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, vk);
            _hotkeyRegistrationStatus[keyName] = ok;
            if (!ok)
            {
                App.Log($"[HotKey] '{keyName}' (VK=0x{vk:X2}) unavailable (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }
    }

    private void UnregisterAllHotKeys()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (var (id, _, _) in HotKeyBindings)
        {
            UnregisterHotKey(_hwnd, id);
        }
    }

    private void UpdateVolumeKeyHook()
    {
        bool enable = (_settingsService?.Current.EnableVolumeKeyOsd ?? false) ||
                      (_settingsService?.Current.EnableLockKeyHud ?? true);
        if (enable && _llKeyboardHook == IntPtr.Zero)
        {
            try
            {
                _llKeyboardProc = LowLevelKeyboardCallback;
                IntPtr hMod = GetModuleHandle(null);
                _llKeyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _llKeyboardProc, hMod, 0);
            }
            catch (Exception ex)
            {
                App.Log($"[VolumeHook] SetWindowsHookEx failed: {ex.Message}");
            }
        }
        else if (!enable && _llKeyboardHook != IntPtr.Zero)
        {
            UninstallVolumeKeyHook();
        }
    }

    private void UninstallVolumeKeyHook()
    {
        if (_llKeyboardHook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_llKeyboardHook); } catch { }
            _llKeyboardHook = IntPtr.Zero;
        }
    }

    private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode is VK_VOLUME_UP or VK_VOLUME_DOWN or VK_VOLUME_MUTE)
                {
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await Task.Delay(35);
                            if (_isClosing) return;
                            if (_machine.State != IslandState.Expanded && (_settingsService?.Current.EnableVolumeKeyOsd ?? false))
                            {
                                OnVolumeChanged(_volumeService.CurrentVolume, _volumeService.IsMuted);
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Log($"[KeyboardHook] Volume OSD dispatch error: {ex.Message}");
                        }
                    });
                }
            }
            else if (msg is WM_KEYUP or WM_SYSKEYUP)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode is VK_CAPITAL or VK_NUMLOCK or VK_SCROLL && (_settingsService?.Current.EnableLockKeyHud ?? true))
                {
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await Task.Delay(15);
                            if (_isClosing) return;
                            OnLockKeyToggled(vkCode);
                        }
                        catch (Exception ex)
                        {
                            App.Log($"[KeyboardHook] Lock key HUD dispatch error: {ex.Message}");
                        }
                    });
                }
            }
        }
        return CallNextHookEx(_llKeyboardHook, nCode, wParam, lParam);
    }

    private void OnLockKeyToggled(int vkCode)
    {
        short state = GetKeyState(vkCode);
        bool isOn = (state & 0x0001) != 0;

        string title;
        string subtitle;
        string glyph;
        Color color = isOn ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0x8E, 0x8E, 0x93);

        switch (vkCode)
        {
            case VK_CAPITAL:
                title = isOn ? "Caps Lock On" : "Caps Lock Off";
                subtitle = isOn ? "UPPERCASE active" : "lowercase active";
                glyph = "\uE8B0";
                break;
            case VK_NUMLOCK:
                title = isOn ? "Num Lock On" : "Num Lock Off";
                subtitle = isOn ? "Keypad numbers enabled" : "Keypad navigation active";
                glyph = "\uE8EF";
                break;
            case VK_SCROLL:
                title = isOn ? "Scroll Lock On" : "Scroll Lock Off";
                subtitle = isOn ? "Scrolling locked" : "Scrolling normal";
                glyph = "\uE74B";
                break;
            default:
                return;
        }

        _machine.PushToast(new ToastContent(
            glyph,
            title,
            subtitle,
            color,
            TimeSpan.FromSeconds(1.8),
            Priority: ToastPriority.Feedback,
            Category: "hud_lock"));
    }

    private void OnInputLanguageChanged(IntPtr hkl)
    {
        try
        {
            ushort langId = unchecked((ushort)(hkl.ToInt64() & 0xFFFF));
            if (langId == 0) return;

            var culture = System.Globalization.CultureInfo.GetCultureInfo(langId);
            string code = culture.TwoLetterISOLanguageName.ToUpperInvariant();
            string name = culture.NativeName;

            _machine.PushToast(new ToastContent(
                "\uE774",
                $"{code} \u00B7 {culture.EnglishName}",
                name,
                Color.FromRgb(0x0A, 0x84, 0xFF),
                TimeSpan.FromSeconds(2.0),
                Priority: ToastPriority.Feedback,
                Category: "hud_keyboard"));
        }
        catch
        {
            // Ignore unrecognized language identifier
        }
    }

    private void OnHotKey(int id)
    {
        switch (id)
        {
            case HotkeyStopwatch: ToggleStopwatch(); break;
            case HotkeyToast: ShowNextDemoToast(); break;
            case HotkeyMedia: _mediaService.TogglePlayPause(); break;
            case HotkeyClipboard: SwitchExpandedView(ExpandedView.Clipboard); break;
            case HotkeyCalc: SwitchExpandedView(ExpandedView.Calc); break;
            case HotkeyAudio: SwitchExpandedView(ExpandedView.Audio); break;
            case HotkeyPower: SwitchExpandedView(ExpandedView.Battery); break;
            case HotkeyRecord: ToggleVoiceRecording(); break;
            case HotkeyPeripherals: SwitchExpandedView(ExpandedView.Peripherals); break;
            case HotkeyNetwork: SwitchExpandedView(ExpandedView.Network); break;
            case HotkeyNotifications: SwitchExpandedView(ExpandedView.Notifications); break;
            case HotkeySettings: SwitchExpandedView(ExpandedView.Settings); break;
            case HotkeySystemHud: SwitchExpandedView(ExpandedView.SystemHud); break;
            case HotkeyQuickPaste:
                if (_dropletRegistry.IsEnabled("clipboard_pro"))
                    SummonClipboardPopupWindow();
                break;
            case HotkeyCapture:
                if (_dropletRegistry.IsEnabled("element_capture"))
                    LaunchElementCaptureOverlay();
                break;
            case HotkeyNotes:
                if (_dropletRegistry.IsEnabled("notes"))
                    SwitchExpandedView(ExpandedView.Notes);
                break;
            case HotkeyTuck: ToggleTuck(); break;
            case HotkeyDock: CycleDockPosition(); break;
            case HotkeyMonitor: SwitchToNextMonitor(); break;
            case HotkeyMicMute: ToggleMicMute(); break;
            case HotkeyPromptApprove: TriggerActivePromptDecision("Approve"); break;
            case HotkeyPromptReject: TriggerActivePromptDecision("Reject"); break;
            case HotkeyToggleFloat: ToggleFloatingMode(); break;
            case HotkeyQuit: Application.Current.Shutdown(); break;
        }
    }

}
