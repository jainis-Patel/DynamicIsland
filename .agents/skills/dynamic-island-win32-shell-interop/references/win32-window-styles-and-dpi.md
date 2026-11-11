# Win32 Window Styles, Layered Hit-Testing & DPI Reference

This reference covers the specific Win32 constants, flags, and messaging routines used by Dynamic Island in `IslandWindow.Interop.cs`.

---

## 1. Extended Window Styles (`WS_EX_*`)

To render a borderless, transparent, non-taskbar desktop overlay:

```csharp
const int WS_EX_TOOLWINDOW = 0x00000080; // Excludes the window from Alt+Tab and the Taskbar
const int WS_EX_LAYERED    = 0x00080000; // Enables per-pixel alpha transparency
const int WS_EX_NOACTIVATE = 0x08000000; // Prevents window from stealing foreground focus on click
```

In `IslandWindow.Interop.cs`:
```csharp
int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
```

---

## 2. Hit-Testing & Message Codes

| Code | Value | Meaning |
|---|---|---|
| `WM_NCHITTEST` | `0x0084` | Sent to determine what part of the window corresponds to a particular screen coordinate |
| `HTCLIENT` | `1` | Cursor is in the interactive client area; route mouse clicks to WPF controls |
| `HTTRANSPARENT` | `-1` | Cursor is in a transparent area; Windows passes clicks directly to windows underneath |
| `WM_DPICHANGED` | `0x02E0` | Sent when window moves to a monitor with different DPI or scaling factor |

---

## 3. DPI Conversion Utilities

When converting physical pixels (returned by `GetMonitorInfo` or `GetCursorPos`) to logical WPF DIPs:

```csharp
public static double PixelsToDips(int pixels, double dpiScale)
{
    return pixels / dpiScale;
}

public static int DipsToPixels(double dips, double dpiScale)
{
    return (int)Math.Round(dips * dpiScale);
}
```

Where `dpiScale = visualDpi.DpiScaleX` (e.g. $1.0$ for 100%, $1.25$ for 125%, $1.5$ for 150%).
