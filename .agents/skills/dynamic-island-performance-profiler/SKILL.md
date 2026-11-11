---
name: dynamic-island-performance-profiler
description: >-
  Use this skill when auditing, profiling, or optimizing memory consumption, GC pressure,
  and idle CPU usage in Dynamic Island. Details how to maintain near-zero idle CPU (< 0.1%)
  and low RAM (< 75MB) by unhooking render loops, enforcing frozen WPF ThemeBrushes,
  monitoring SkiaSharp/Lottie allocations, preventing floating window memory leaks,
  and executing diagnostic flags (--diagnose-memory, --diagnose-window, --diagnose-software).
---

# Dynamic Island Performance & Memory Profiler

A primary pain point discussed on Reddit regarding desktop overlays (like Dynamic Island and Notch clones) is resource hogging. Users frequently criticize Electron-based overlays that consume 200MB–400MB of RAM and 1%–3% CPU at idle. 

Dynamic Island is engineered to run at **near-zero idle CPU (< 0.1%)** and **low working set (< 75MB)**. This runbook establishes the profiling procedures, diagnostic tooling, and architectural rules necessary to preserve this performance budget.

---

## Performance Targets

| Metric | Target (Idle / Compact) | Target (Active Expanded / Music) | Warning Threshold |
|---|---|---|---|
| **CPU Usage** | `< 0.1%` (0% on most cores) | `< 1.2%` (FFT audio + spring) | `> 0.5%` idle |
| **Private Working Set** | `< 65 MB` | `< 95 MB` (with album art cached) | `> 120 MB` |
| **GC Allocations** | `0 bytes / frame` during idle | `< 1 KB / frame` during animation | Any Gen 2 GC in idle |
| **Animation Frame Rate** | `60 Hz – 240 Hz` (matching display) | Synchronized to refresh rate | Frame drops / jank |

---

## Profiling Workflow & Diagnostic Modes

The application embeds built-in diagnostic modes in [Program.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Program.cs). Run them using the provided script [DiagnosePerformance.ps1](./scripts/DiagnosePerformance.ps1):

### 1. Diagnose Memory & Heap Fragmentation
```powershell
dotnet run -c Release -- --diagnose-memory
```
* Reports: Working Set, Private Bytes, GC Heap Size, Total Committed Bytes, and per-generation sizes/fragmentation.
* **Acceptance criteria**: Gen 0, Gen 1, and Gen 2 sizes should remain minimal without lingering allocations.

### 2. Diagnose Window & UI Component Working Set
```powershell
dotnet run -c Release -- --diagnose-window
```
* Instantiates `IslandWindow`, initializes all sub-views, measures memory after 4 seconds of warm-up, and terminates cleanly.
* **Acceptance criteria**: WorkingSet must remain below 90 MB.

### 3. Diagnose Software vs Hardware Render Modules
```powershell
dotnet run -c Release -- --diagnose-software
```
* Forces software rendering (`RenderMode.SoftwareOnly`) and dumps loaded graphics modules (`d3d9`, `d3d11`, `nvwgf2umx`, `SkiaSharp`).

---

## The Zero-Idle-CPU Rules

When writing or reviewing code, enforce the following core performance rules:

### 1. Detach `CompositionTarget.Rendering`
The WPF rendering loop ticks at the monitor refresh rate (up to 240Hz). Never keep `CompositionTarget.Rendering` subscribed continuously.
* In [IslandWindow.Animation.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/IslandWindow.Animation.cs), `EnsureRendering()` hooks the event only when `_rendering` is false.
* When all springs are `IsAtRest` and no active Lottie/Pet animations are scampering, `CompositionTarget.Rendering` is immediately detached.

### 2. Freeze All Freezables (`Brush`, `Pen`, `Geometry`)
Unfrozen WPF brushes require thread-affinity checks and re-evaluation. 
* All application brushes in [ThemeBrushes.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Core/ThemeBrushes.cs) and easing functions must have `.Freeze()` called during initialization.

### 3. Use `DispatcherPriority.Background` for Timers
Never run polling timers at `DispatcherPriority.Normal` or `Send`. Use `DispatcherPriority.Background` or `ContextIdle` so UI rendering, cursor hit-testing, and window dragging are never delayed.

### 4. Zero-Allocation Audio Buffers
* Audio FFT analysis in `Services/AudioSpectrum.cs` uses pre-allocated pinned arrays and lock-free atomic reads.
* `Services/KeyboardSoundService.cs` uses a pre-allocated `_scaledBuffer` to prevent Gen 0 GC garbage on rapid keystrokes.

### 5. Memory Management & GC Trimming
Review [IslandMemoryManager.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Core/IslandMemoryManager.cs) which monitors process working set and gently trims unreferenced memory (`EmptyWorkingSet`) when the island has remained in `Idle` state for over 60 seconds.

### 6. Secondary Floating Window Lifecycle (`OnClosed`)
Secondary windows (`MiniPlayerWindow`, `BasketWindow`, `NotesPopupWindow`, `ClipboardPopupWindow`, `PinnedCaptureWindow`) must override `OnClosed`:
* Unsubscribe all event handlers hooked to singleton services (`MediaSessionService`, `LyricsService`, `VolumeService`).
* Stop and unhook any `DispatcherTimer` instances (`_posTimer`, `_eqTimer`, `_autoCloseTimer`).
* Cancel any active `CancellationTokenSource`.
Failure to do so leaks the entire window visual tree and keeps background timers firing forever.

### 7. Fast-Exit Hooks for Low-Level Windows Hooks
When installing global mouse or keyboard hooks (`WH_MOUSE_LL` in `JiggleBasketService`, `WH_KEYBOARD_LL` in `KeyboardSoundService`):
* The hook proc must execute in nanoseconds: test key states immediately (e.g. `GetAsyncKeyState(VK_LBUTTON)`) and return `CallNextHookEx` on the first line when inactive.
* Hooks must be uninstalled immediately whenever their corresponding droplet is disabled in `DropletRegistry`.

---

## Detailed References

* [WPF Memory & Render Rules](./references/wpf-memory-and-render-rules.md)
* [Performance Diagnostic Script](./scripts/DiagnosePerformance.ps1)
