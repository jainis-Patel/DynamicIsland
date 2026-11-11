# Liquid Glass Discovery & Architecture Analysis

## 1. Executive Summary

This document performs the architectural discovery required for integrating a **real, GPU-based, refraction-driven Liquid Glass rendering engine** into **Dynamic Island for Windows**.

Historically, the application simulated "Liquid Glass" via frozen linear gradients (`ThemeBrushes.PillGlassSheenLiquidDark`, `PillBackgroundLiquidDark`, `PillStrokeLiquidDark`), drop shadows, and semi-transparent alpha overlays. While visually polished, this falls squarely into the category of CSS/XAML-style glassmorphism rather than physically-based optical refraction.

The new system introduces a native Direct3D 11 / HLSL pixel shader pipeline executing per-pixel Signed Distance Field (SDF) evaluation, surface normal reconstruction from edge proximity, Snell's law refraction against live captured backdrops, mip/Gaussian backdrop blurring, specular rim illumination, and smooth-union ($s\min$) multi-body fluid merging.

---

## 2. Platform & UI Framework Inspection

| Property | Detail | Notes |
|---|---|---|
| **UI Framework** | WPF (.NET 8.0 Windows Desktop) | `UseWPF=true`, XAML + C# 12 |
| **Target Framework** | `net8.0-windows10.0.19041.0` | Exposes native Windows Runtime (WinRT) projections |
| **Supported OS Version** | `10.0.17763.0` (Min) / `10.0.19041.0+` (Target) | Windows 10 (2004+) & Windows 11 fully supported |
| **Process Model** | 64-bit Native WinExe (`win-x64`) | Single-instance with IPC and named mutex |
| **DPI Awareness** | Per-Monitor V2 (`app.manifest`) | `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2` |
| **Window Architecture** | Transparent, Borderless Overlay | `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`, `Topmost="True"` |

---

## 3. Window Nature & Overlay Topology

The primary window (`IslandWindow.xaml`) is an **always-on-top, borderless desktop overlay** pinned to the top-center of the active display (or MacBook-style physical notch). It floats directly above:
1. Third-party desktop application windows (browsers, IDEs, terminal, office suites).
2. The Windows desktop wallpaper and icons.
3. Fullscreen or borderless games (suppressed automatically when exclusive gaming is detected).

Because `IslandWindow` sits over external operating system content, **the glass must refract other windows and the desktop beneath it**.

In addition, Dynamic Island features **secondary floating windows** and **in-island expanded cards**:
- Floating companion windows (`BasketWindow`, `NotesPopupWindow`, `ClipboardPopupWindow`, `MiniPlayerWindow`, `PinnedCaptureWindow`) also hover over desktop content.
- In-island cards and chips (inside `ExpandedView` panels, widget controls, and setup wizards) sit over the app's own internal dark or light canvas.

### Path Decision: Hybrid Architecture (PATH B Primary + PATH A In-App)

Per the project specification:
- **PATH B (Desktop Capture)** is the primary backdrop provider for the main floating pill, secondary split-pill droplet, floating dock, and companion floating windows. It captures the live screen content behind each glass shape using `Windows.Graphics.Capture`, excludes the overlay HWND (`WDA_EXCLUDEFROMCAPTURE`), crops the region to the bounding box plus refraction slop, and feeds GPU textures directly into D3D11 without CPU readback.
- **PATH A (In-App Backdrop)** is the provider for internal nested surfaces (such as cards, dialog backdrops, and chips) refracting the app's own content.
- **Unified Interface**: Both providers implement `IBackdropProvider`, sharing the identical `GlassShaderCore.hlsl` shader and `GlassRenderer`.

---

## 4. Complete Inventory of Glass Surfaces

The following table catalogs every UI surface earmarked for Liquid Glass treatment:

| Surface / Component | File Path | Role & Shape | Path |
|---|---|---|---|
| **Main Island Pill** | `IslandWindow.xaml` (`PillBackground`, `PillStroke`) | Morphing capsule / rounded-rect (idle 148x30 up to expanded 640x340) | **Path B** |
| **Secondary Droplet** | `IslandWindow.xaml` (`SecondaryDroplet`) | Liquid circular satellite (36x36 circle/capsule, merges with main pill via $s\min$) | **Path B** |
| **Floating Droplet Dock** | `IslandWindow.xaml` (`DockBorder`, `DropletDockPanel`) | Continuous rounded-rect floating utility dock | **Path B** |
| **Jiggle Floating Basket** | `Views/BasketWindow.xaml` (`BasketBorder`) | Floating circular/capsule drag-drop target (180x180) | **Path B** |
| **Mini Detached Player** | `Views/MiniPlayerWindow.xaml` (`PlayerCard`) | Compact floating media HUD (320x96 capsule) | **Path B** |
| **Notes Scratchpad Popup** | `Views/NotesPopupWindow.xaml` (`NotesRootBorder`) | Detached desktop notes surface (380x320) | **Path B** |
| **Clipboard Quick-Paste** | `Views/ClipboardPopupWindow.xaml` (`PopupBorder`) | Floating history window (360x420) | **Path B** |
| **Pinned Element Capture** | `Views/PinnedCaptureWindow.xaml` (`PinBorder`) | Floating screenshot pin card | **Path B** |
| **In-Island Widget Cards** | `Views/Widgets/MediaView.xaml`, `AudioView.xaml`, `BatteryView.xaml`, `CalcView.xaml`, `CalendarTaskView.xaml`, `SystemHudView.xaml`, etc. | Nested telemetry & controls cards within expanded island | **Path A** |
| **Capsule Action Buttons** | `Views/Widgets/*.xaml`, `IslandWindow.xaml` (Play/pause, clear, toggle buttons) | Small pill buttons with high-index rim refraction | **Path A / B** |
| **Setup & Uninstall Wizards** | `Views/SetupWindow.xaml`, `Views/UninstallWindow.xaml` | Modal glass card backdrop | **Path A** |

---

## 5. Rendering Pipeline & Interop Today

1. **WPF Visual Tree & Milcore**:
   - `IslandWindow` runs standard WPF double-buffered rendering on a layered window (`WS_EX_LAYERED`).
   - Click-through behavior is managed via Win32 `WM_NCHITTEST` in `IslandWindow.Interop.cs`, returning `HTCLIENT` within the active pill hot-zone and `HTTRANSPARENT` everywhere else.
2. **Animation Loop**:
   - Driven by `CompositionTarget.Rendering` in `IslandWindow.Animation.cs`.
   - Closed-form analytical springs (`Core/Spring.cs`) update `Pill.Width`, `Pill.Height`, and `PillBackground.CornerRadius` per frame during motion.
   - Detached immediately when springs settle (`IsAtRest`) to enforce the `< 0.1%` idle CPU requirement.
3. **DPI & Multi-Monitor**:
   - Monitored displays are tracked via `MonitorFromWindow` / `EnumDisplayMonitors`.
   - `VisualTreeHelper.GetDpi(this).DpiScaleX` scales layout dimensions; Win32 interop converts DIPs to physical pixels via `NativeMethods.LogicalToPhysical`.
4. **Third-Party Packages & DirectX Status**:
   - `NAudio.Wasapi` (Audio spectrum capture).
   - `PDFsharp` (Document conversion).
   - `Vortice.Direct3D11` & `Vortice.DirectComposition` (Added for native GPU shader compilation, swap chain presentation, and D3D11 interop).
   - Windows SDK tools (`fxc.exe` / `dxc.exe` located at `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\fxc.exe`) are available for build-time HLSL bytecode generation.

---

## 6. Performance Budget & Non-Negotiables

| Metric | Target | Enforcement Strategy |
|---|---|---|
| **Idle CPU** | `< 0.1%` | When desktop backdrop is static and pill is at rest, D3D rendering is paused; zero frames pushed. |
| **Active FPS** | `60 - 240 FPS` | D3D11 swap chain synchronized to monitor refresh rate; GPU shader pass executes in `< 0.8 ms`. |
| **Private Working Set** | `< 75 MB` | GPU buffers and frame pools kept in VRAM; zero CPU staging readbacks. |
| **Capture Latency** | `1 - 2 frames` | `Direct3D11CaptureFramePool.CreateFreeThreaded` with 2 buffers, processing immediately upon `FrameArrived`. |
| **Quality Fallback** | Graceful degradation | On Windows 10 < 2004 or battery saver / secure desktop (UAC), fallback to Mica/Acrylic or pre-frozen linear brushes. |

---

## 7. Next Steps (Implementation Sequence)

1. **Phase 2 - Shader Core & Math Foundation**:
   - Create `src/LiquidGlass/Shaders/GlassShaderCore.hlsl` implementing:
     - SDFs for Rounded Rectangle, Capsule, Squircle, Circle.
     - Smooth-union ($s\min$) polynomial blending.
     - Analytic surface normal derivation: $\mathbf{n} = \text{normalize}\left(\nabla d \cdot a, \sqrt{1 - a^2}\right)$.
     - Snell's law refraction: $\mathbf{R} = \text{refract}((0,0,-1), \mathbf{n}, 1/\text{ior})$.
     - Separable Gaussian / mip blur sampling of backdrop.
     - Directional specular rim, tint, saturation, and optional chromatic dispersion.
   - Configure MSBuild target to compile `GlassShaderCore.hlsl` to embedded bytecode (`.cso`).
2. **Phase 3 - Core Model & Renderer**:
   - Implement `GlassSettings` (immutable record + builder) and `GlassPresets`.
   - Implement `GlassRenderer` (D3D11 device, vertex/pixel pipeline, constant buffer, separable blur downsampler).
3. **Phase 4 - Backdrop Providers**:
   - `IBackdropProvider` interface.
   - `DesktopCaptureBackdrop` (Path B) with `Windows.Graphics.Capture`, `WDA_EXCLUDEFROMCAPTURE`, and borderless capture.
   - `VisualBackdrop` (Path A) for in-app surfaces.
4. **Phase 5 - GlassHost & Playground**:
   - Standalone `GlassPlayground` window with live parameter tuning, test patterns, and golden image verification.
   - `GlassHost` integration wrapper.
5. **Phase 6 - Application Rollout & Verification**:
   - Integrate with `IslandWindow` and secondary surfaces.
   - Profile performance, memory, and execute full test suite.
