---
name: dynamic-island-win32-shell-interop
description: >-
  Use this skill when working on low-level Windows integration, Win32 window styles,
  Per-Monitor V2 DPI scaling, multi-monitor display placement, borderless/exclusive
  fullscreen game detection, hardware privacy dot registry hooks, or global hotkeys.
  Helps resolve click-through bugs, multi-DPI blur, and gaming overlay auto-suppression.
---

# Dynamic Island Win32 Shell & Interop Runbook

Because Dynamic Island acts as a persistent desktop overlay anchored to the top of the user's primary or secondary screen, it interacts directly with low-level Windows Shell APIs, the DWM compositor, and multi-monitor DPI subsystems.

Reddit discussions regarding Windows overlays frequently cite issues with:
1. **Accidental Click Interception**: Transparent areas around the pill blocking mouse clicks to browser tabs, address bars, or game controls.
2. **Multi-Monitor DPI Blur**: Looking sharp on a 4K 150% scaling display, but blurry or wrongly positioned on a 1080p secondary display.
3. **Gaming Overlay Interference**: Popping on top of fullscreen DirectX games or movies.
4. **Hardware Privacy Monitoring**: High CPU usage from polling webcam/microphone state.

Dynamic Island solves these challenges through native Win32 interop patterns implemented across `IslandWindow.Interop.cs`, `Core/FullscreenWatcher.cs`, and `Services/DisplayTopologyService.cs`.

---

## Key Interop Subsystems

```
┌─────────────────────────────────┐
│     Desktop Display System      │
├─────────────────────────────────┤
│ • Per-Monitor V2 DPI (DpiAware) │ ──► [DisplayTopologyService.cs]
│ • Multi-Monitor Enumeration     │
│ • Fullscreen Game Detection     │ ──► [FullscreenWatcher.cs]
│ • Transparent Window Hit-Test   │ ──► [IslandWindow.Interop.cs] (WM_NCHITTEST)
│ • Privacy Registry Watcher      │ ──► [PrivacyIndicatorService.cs]
└─────────────────────────────────┘
```

---

## 1. Non-Blocking Transparent Hit-Testing (`WM_NCHITTEST`)

A common bug in transparent WPF windows (`AllowsTransparency="True"`) is that the transparent margins consume mouse clicks meant for applications underneath.

### Implementation in `IslandWindow.Interop.cs`:
In the Win32 window message hook (`WndProc`):
* Handle `WM_NCHITTEST (0x0084)`:
  1. Convert screen coordinates $(x, y)$ to window-relative coordinates using `PointFromScreen`.
  2. Perform visual hit-testing against `PillContainer` or interactive controls.
  3. If the mouse is within the interactive pill or expanded card, return `HTCLIENT (1)`.
  4. If the mouse is in the transparent margin outside the pill, return `HTTRANSPARENT (-1)`.
* **Result**: Windows passes the mouse click through to whatever window is behind the transparent area (e.g. browser tabs, title bars).

---

## 2. Borderless & Exclusive Fullscreen Detection

Gamers on Reddit want overlays to vanish completely while playing games or watching fullscreen Netflix/YouTube.

### Implementation in `Core/FullscreenWatcher.cs`:
`FullscreenWatcher` polls at a 1-second background interval without requiring invasive DirectX hooks or anti-cheat triggers:
1. Retrieves the active foreground window (`GetForegroundWindow()`).
2. Discards system windows (Desktop, Shell, Taskbar, Island itself).
3. Queries window dimensions via `GetWindowRect(hwnd, out RECT rect)`.
4. Compares the window rect against the target monitor bounds (`GetMonitorInfo`):
   ```csharp
   bool matchesMonitor = rect.Left <= monitorRect.Left &&
                          rect.Top <= monitorRect.Top &&
                          rect.Right >= monitorRect.Right &&
                          rect.Bottom >= monitorRect.Bottom;
   ```
5. If a matching borderless or exclusive fullscreen window is found, invokes `Changed(true)`, transitioning `IslandStateMachine` to `IslandState.Hidden`.

---

## 3. Per-Monitor V2 DPI Awareness

Dynamic Island declares Per-Monitor V2 DPI awareness in [app.manifest](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/app.manifest):

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">permonitorv2,permonitor</dpiAwareness>
  </windowsSettings>
</application>
```

### Positioning Coordinates:
* WPF coordinates are in device-independent units (96 DPI), while Win32 APIs (`GetCursorPos`, `GetWindowRect`, `MonitorFromWindow`) operate in physical device pixels.
* [DisplayTopologyService.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Services/DisplayTopologyService.cs) translates physical monitor coordinates into WPF logical DIPs using `VisualTreeHelper.GetDpi(window)`.

---

## 4. Hardware Privacy Dot Registry Watcher

Instead of polling audio/video capture hardware, [PrivacyIndicatorService.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Services/PrivacyIndicatorService.cs) hooks into the Windows privacy registry keys via `RegNotifyChangeKeyValue`:

```text
SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam
SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone
```

* **Zero-CPU Efficiency**: The thread sleeps until the Windows kernel signals a registry modification event, consuming **0.0% CPU** while idle.

---

## 5. Low-Level Hooks & Secondary Floating Window Styles

### Low-Level Mouse & Keyboard Hooks:
* `WH_MOUSE_LL` in [JiggleBasketService.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Services/JiggleBasketService.cs) detects cursor shake gestures during file drags. Fast exit on non-pressed `VK_LBUTTON`.
* `WH_KEYBOARD_LL` in [KeyboardSoundService.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Services/KeyboardSoundService.cs) synthesizes switch acoustics.
* **Rule**: Hooks are strictly installed *only* when their respective droplet is enabled in `DropletRegistry`, and callbacks must execute in nanoseconds.

### Secondary Popup Window ExStyles:
Secondary utility windows (`BasketWindow`, `ClipboardPopupWindow`, `NotesPopupWindow`) must configure non-activating tool window styles in `OnSourceInitialized`:
```csharp
IntPtr hwnd = new WindowInteropHelper(this).Handle;
long ex = GetExStyle(hwnd);
ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
ex &= ~WS_EX_APPWINDOW;
SetExStyle(hwnd, ex);
```
This prevents floating windows from stealing keyboard focus from fullscreen apps or game overlays and keeps Alt+Tab clean.

---

## Detailed References

* [Win32 Window Styles & DPI Reference](./references/win32-window-styles-and-dpi.md)
