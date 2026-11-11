---
name: dynamic-island-droplet-ecosystem
description: >-
  Use this skill when developing, testing, or extending modular Droplets in Dynamic Island
  (such as Jiggle Floating Basket, Drop Shelf, File Converter & PDF Merger, Paste Queue,
  LAN Phone Bridge, Element Capture, Notes Scratchpad, or Mech Keyboard Sounds). Covers the
  DropletRegistry lifecycle, zero-allocation toggle mechanics, drag-and-drop file pipelines,
  and secondary floating window coordination.
---

# Dynamic Island Droplet Ecosystem (Droppy Parity)

The **Droplet Ecosystem** brings modular micro-utilities to Dynamic Island, inspired by popular macOS utility Droppy, but engineered for Windows with **true zero-idle overhead**. When a droplet is disabled, its background hooks, Named Pipes, TCP listeners, and timers are completely uninstalled—consuming 0 bytes of RAM and 0% CPU.

---

## Droplet Architecture

```
┌─────────────────────────────────┐
│     Core/DropletRegistry.cs     │ ◄─── Managed by SettingsView (Modular Toggles)
└───────────────┬─────────────────┘
                │
        ┌───────┴───────┬──────────────────┐
        ▼               ▼                  ▼
┌──────────────┐ ┌───────────────┐ ┌────────────────┐
│ Drop Shelf   │ │ Jiggle Basket │ │ File Converter │
│ Services/    │ │ Views/        │ │ Services/      │
│ DropShelf    │ │ BasketWindow  │ │ FileAction     │
│ Service.cs   │ │ .xaml         │ │ Pipeline.cs    │
└──────────────┘ └───────────────┘ └────────────────┘
        │               │                  │
        ▼               ▼                  ▼
┌──────────────┐ ┌───────────────┐ ┌────────────────┐
│ Phone Bridge │ │ Paste Queue   │ │ Element Capture│
│ Services/    │ │ Services/     │ │ Views/Capture  │
│ LanQuick     │ │ Clipboard     │ │ OverlayWindow  │
│ Share.cs     │ │ History.cs    │ │ .xaml          │
└──────────────┘ └───────────────┘ └────────────────┘
```

---

## 1. Registering a New Droplet

In [Core/DropletRegistry.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Core/DropletRegistry.cs):

```csharp
Register(new DropletDescriptor
{
    Id = "my_droplet",
    Name = "My Droplet Name",
    Description = "One-line clear description of the utility",
    Glyph = "\uE71D", // Segoe Fluent Icons glyph
    Category = DropletCategory.Productivity, // Productivity, Media, System, or Capture
    TargetView = ExpandedView.MyWidget,      // Optional navigation link
    DockPinKey = "mywidget",                 // Optional dock strip pin
    EstimatedRamKb = 48,                     // Realistic memory footprint estimate
    IsDefaultEnabled = true,
    OnActivate = () => _myService?.SetEnabled(true),
    OnDeactivate = () => _myService?.SetEnabled(false)
});
```

### Zero-Allocation Contract:
* `OnActivate`: Installs low-level hooks, initializes audio providers, or binds sockets.
* `OnDeactivate`: Immediately tears down hooks (`UnhookWindowsHookEx`), closes sockets, and frees memory.

---

## 2. File Action Pipeline (`FileActionPipeline.cs`)

When users drag files onto the **Drop Shelf** or **Floating Basket**, the `FileActionPipeline` provides offline, zero-dependency processing:

* **Image Normalization**: Converts PNG, JPG, BMP, WebP, HEIC via WIC; strips EXIF metadata blocks.
* **PDF Conversion & Merging**: Uses PDFsharp to combine images or merge multiple PDF documents into single clean files.
  * *Important*: Keep all intermediate memory streams open until `doc.Save()` finishes writing the output file.
* **H.264 Video Compression**: Uses native Windows `MediaTranscoder` for hardware 720p/1080p MP4 exports.
* **Batch Timestamp Copies**: Non-destructive file renaming with formatted ISO timestamps.
* **Data URI Base64 Encoding**: Encodes images directly to developer-friendly `data:image/png;base64,...` text.

---

## 3. Secondary Floating Windows

Droplets often spawn floating top-level utility windows:
* `Views/BasketWindow.xaml`: Summoned by mouse jiggling while dragging files.
* `Views/CaptureOverlayWindow.xaml`: Snipping tool with rectangular, arrow, and redaction annotations.
* `Views/PinnedCaptureWindow.xaml`: Sticky floating screenshot pin with wheel zoom and copy.
* `Views/ClipboardPopupWindow.xaml`: Quick picker and sequential paste queue.
* `Views/NotesPopupWindow.xaml`: Floating scratchpad notepad.
* `Views/MiniPlayerWindow.xaml`: Detachable desktop music player.

### Floating Window Rules:
1. **Tool Window ExStyles**: Set `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` so popups don't steal keyboard focus from active games/apps or pollute Alt+Tab.
2. **Per-Monitor V2 DPI**: Calculate bounds using `GetDpiForMonitor` and clamp coordinates within `rcWork` of `MonitorFromPoint`.
3. **Clean Teardown**: Always override `OnClosed` to stop `DispatcherTimer` instances and unhook singleton service delegates.

---

## 4. Autonomous Phone Bridge & LAN QuickShare

In [Services/LanQuickShareService.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Services/LanQuickShareService.cs):
* Ephemeral local HTTP server on port `8989` via pure user-mode `TcpListener` (no admin URL ACL required).
* Uses high-entropy 64-bit cryptographic tokens in the query string (`?t=<token>`).
* Serves embedded zero-dependency PWA allowing iOS Safari / Android Chrome to upload files to Drop Shelf and sync clipboard text bi-directionally.
* QR Code generated natively using `Core/QrCode.cs` (ISO/IEC 18004 2D matrix encoding).
