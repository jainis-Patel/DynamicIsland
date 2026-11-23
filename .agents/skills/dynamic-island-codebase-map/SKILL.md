---
name: dynamic-island-codebase-map
description: >-
  Quick-reference codebase map for Dynamic Island. Use this skill FIRST before
  running directory listings or broad searches whenever you need to locate files,
  identify which IslandWindow partial, Service, Core module, View, or Interop file
  owns a feature, check project stack/entry points, or find build, self-test, and
  diagnostic commands. Minimizes exploratory tool calls for small tasks.
---

# Dynamic Island Codebase Map

Use this map to jump directly to the right file without broad directory scans or `grep` discovery passes.

---

## Project Stack & Entry Points

* **Project**: [DynamicIsland.csproj](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/DynamicIsland.csproj) — `.NET 8` (`net8.0-windows10.0.19041.0`), WPF (`UseWPF=true`), `win-x64`, `Nullable=enable`, `AllowUnsafeBlocks=true`, root namespace `DynamicIsland`.
* **NuGet Dependencies**: `NAudio.Wasapi`, `PDFsharp`, `Vortice.Direct3D11`, `Vortice.DirectComposition`.
* **Entry Point**: `Program.cs` (CLI flags & single-instance/startup) → `App.xaml.cs` (service initialization) → `IslandWindow.xaml` / `IslandWindow.xaml.cs`.
* **Excluded from Build** (do not edit or search here for active code): `scratch/`, `old/`, `dist/`, `dist_*/`, `publish/`, `publish_*/`.

---

## Build, Self-Test & Diagnostic Commands

* **Build**: `dotnet build DynamicIsland.csproj`
* **Deterministic Self-Tests**: `DynamicIsland.exe --self-test` (or `--self-test --filter=<pattern>`)
  * Runner: `Core/SelfTestRunner.cs`
  * Test suites in `Core/SelfTests/`: `SelfTestRunner.CoreAndUi.cs`, `SelfTestRunner.DropletsAndSharing.cs`, `SelfTestRunner.LiquidGlassAndUpdates.cs`, `SelfTestRunner.PillarsAndHygiene.cs`, `SelfTestRunner.SystemAndMedia.cs`.
* **Diagnostic Flags** (handled in `Program.cs`):
  * `--diagnose-memory`: Prints process working set, private bytes, and GC heap/generation stats.
  * `--diagnose-window`: Boots `IslandWindow` in diagnostic mode, trims memory after 4s, and prints stats.
  * `--diagnose-software`: Forces `RenderMode.SoftwareOnly` and prints process/GC stats.
* **Batch / PowerShell Scripts**: `BuildInstaller.bat`, `TestDroplets.bat`, `Launch.bat`, `AllowPhoneBridgeFirewall.bat`, `scripts/CleanWorkspace.ps1`.

---

## `IslandWindow` Partial Files (Repo Root)

`IslandWindow` is split into focused partial `.cs` files at the repository root:

* **Core Shell, Routing & Animation**:
  * `IslandWindow.xaml` / `IslandWindow.xaml.cs` — Window shell, initialization, core state.
  * `IslandWindow.Navigation.cs` — Expanded view routing and morphing.
  * `IslandWindow.ViewForwarders.cs` — Event/command forwarding to widget UserControls.
  * `IslandWindow.Animation.cs` — Spring-driven pill/window animation loop.
  * `IslandWindow.Interop.cs` — Win32 hooks, DPI, click-through, window styles.
  * `IslandWindow.Floating.cs` — Secondary floating windows coordination.
* **Feature Partials**:
  * `IslandWindow.AudioEndpoints.cs` — Audio output/input device switching.
  * `IslandWindow.ClipboardCalc.cs` — Clipboard history & quick calculator.
  * `IslandWindow.DropShelf.cs` — Drag-and-drop shelf & file actions.
  * `IslandWindow.Lyrics.cs` — Synchronized lyrics display.
  * `IslandWindow.Media.cs` — Media playback controls & visualizer.
  * `IslandWindow.Network.cs` — Network speed / connectivity UI.
  * `IslandWindow.Notifications.cs` — Windows notification toast/history UI.
  * `IslandWindow.Peripherals.cs` — Bluetooth / peripheral battery & status.
  * `IslandWindow.PowerBattery.cs` — Battery & power plan UI.
  * `IslandWindow.Privacy.cs` — Mic/camera privacy indicator dots.
  * `IslandWindow.SystemHud.cs` — Volume/brightness/caps HUD.
  * `IslandWindow.VirtualPet.cs` — Virtual pet interactions.
  * `IslandWindow.VoiceMemo.cs` — Voice memo recording/playback UI.
  * `IslandWindow.WeatherCalendar.cs` — Weather, calendar & tasks UI.
* **Settings Partials**:
  * `IslandWindow.Settings.cs`, `IslandWindow.Settings.Droplets.cs`, `IslandWindow.Settings.Themes.cs`, `IslandWindow.Settings.Updates.cs`.

---

## Directory Map

### `Core/` — Architecture, Motion, State & Utilities
* **Lifecycle & Routing**: `ServiceContainer.cs`, `ExpandedView.cs`, `DropletRegistry.cs`, `IslandStateMachine.cs`, `ActivityResolver.cs`, `LiveActivityHub.cs`, `CommunityDroplet.cs`.
* **Motion & Rendering**: `Spring.cs`, `BentoMotion.cs`, `Choreographer.cs`, `LiquidUiEngine.cs`, `LiquidGlassTheme.cs`, `ThemeManager.cs`, `ThemeBrushes.cs`, `TactileFeedback.cs`, `IslandNatureThemeControl.cs`.
* **Persistence & Paths**: `AppPaths.cs`, `AtomicJsonStore.cs`, `DebouncedJsonStore.cs`, `SettingsSearchIndex.cs`.
* **Window, Input & Telemetry**: `FullscreenWatcher.cs`, `CursorTracker.cs`, `TouchpadGestureProcessor.cs`, `MagneticDockingEngine.cs`, `IslandMemoryManager.cs`, `HardwareTelemetry.cs`, `DeviceClassifier.cs`, `AppIconLoader.cs`, `Converters.cs`.
* **Updates, Networking & Safety**: `InstallerService.cs`, `UpdateHandoffEngine.cs`, `UpdateIntegrityVerifier.cs`, `UpdateModels.cs`, `SafeClipboard.cs`, `SafeProcessLauncher.cs`, `SharedHttpClient.cs`, `InlineConfirmGuard.cs`, `QrCode.cs`.
* **Subfolders**: `Core/SelfTests/`, `Core/LanShare/`, and `VirtualPetControl.cs` / `VirtualPetControl.Rendering.cs`.

### `Services/` — Background & Platform Services (`IDisposable`)
* **Media & Audio**: `AudioEndpointService.cs`, `AudioSpectrum.cs`, `ColorExtractor.cs`, `LyricsService.cs`, `MediaPlayerBrandService.cs`, `MediaSessionService.cs`, `MediaSongListService.cs`, `VolumeService.cs`, `KeyboardSoundService.cs`, `VoiceMemoService.cs`.
* **System & Hardware**: `BluetoothMonitor.cs`, `BrightnessService.cs`, `DisplayTopologyService.cs`, `NetworkService.cs`, `NotificationListenerService.cs`, `PeripheralDeviceService.cs`, `PowerPlanService.cs`, `PrivacyIndicatorService.cs`, `SystemPerformanceService.cs`, `StartupService.cs`, `WindowSnapService.cs`.
* **Productivity & Utilities**: `CalendarService.cs`, `ClipboardHistoryService.cs`, `CountdownTimerService.cs`, `MeetingModeService.cs`, `NotesService.cs`, `QuickCalcService.cs`, `QuickLauncherService.cs`, `ScreenToolsService.cs`, `TaskService.cs`, `TermiNotchService.cs`, `VirtualPetService.cs`, `WeatherService.cs`.
* **Files, Sharing & IPC**: `DownloadTrackerService.cs`, `DropShelfService.cs`, `FileActionPipeline.cs`, `JiggleBasketService.cs`, `LanQuickShareService.cs`, `LiveActivityIpcService.cs`, `WindowsShareService.cs`.
* **App & Updates**: `CommunityDropletService.cs`, `GitHubReleaseClient.cs`, `SettingsService.cs`, `TrayIconService.cs`, `UpdateManagerService.cs`.

### `Views/` — Floating Windows & Modular Widgets
* **Standalone Windows (`Views/*.xaml`)**: `BasketWindow`, `CaptureOverlayWindow`, `ClipboardPopupWindow`, `DesktopWidgetWindow`, `GlassPlayground`, `MiniPlayerWindow`, `NotesPopupWindow`, `PetFloatWindow`, `PinnedCaptureWindow`, `SetupWindow`, `UninstallWindow`.
* **Widget UserControls (`Views/Widgets/*.xaml`)**: `AudioView`, `BatteryView`, `CalcView`, `CalendarTaskView`, `CaptureStudioView`, `ClipboardView`, `ConverterView`, `LauncherView`, `MechSoundsView`, `MediaView`, `NetworkView`, `NotesView`, `NotificationsView`, `PeripheralsView`, `PetSanctuaryView`, `PhoneBridgeView`, `QuickReplyView`, `SettingsView` (+ `Views/Widgets/Settings/`), `ShelfView`, `SystemHudView`, `TermiNotchView`, `VoiceMemoView`, `WidgetsGalleryView`.

### `Interop/`, `src/`, `Resources/` & `Assets/`
* `Interop/`: `NativeMethods.cs` (P/Invoke Win32 APIs), `WindowBlurHelper.cs` (Acrylic/Mica/DWM).
* `src/LiquidGlass/`: Custom shader effects (`Shaders/*.cso` embedded resources).
* `Resources/`: Embedded resources including `PhoneBridge.html`.
* `Assets/`: `app.ico`, `app.png`, `app_256.png`.

---

## Specialized Sibling Skills (Deep Dives)

When a task goes beyond locating files into subsystem implementation rules, load the relevant skill:
* `dynamic-island-widget-creator` — Adding new `Views/Widgets/` UserControls, `ExpandedView`, and `ServiceContainer` wiring.
* `dynamic-island-droplet-ecosystem` — Modular Droplets, `DropletRegistry`, drag-and-drop pipelines, floating windows.
* `dynamic-island-spring-physics` — `Core/Spring.cs`, frame-rate invariance, `CompositionTarget.Rendering`.
* `dynamic-island-wasapi-media` — GSMTC media tracking, WASAPI loopback FFT, `AudioSpectrum.cs`, LRCLIB lyrics.
* `dynamic-island-win32-shell-interop` — Win32 styles, Per-Monitor V2 DPI, fullscreen detection, privacy hooks.
* `dynamic-island-live-activities-ipc` — Named Pipe IPC (`\\.\pipe\DynamicIsland.Activities`) & priority queuing.
* `dynamic-island-performance-profiler` — Zero-idle-CPU (< 0.1%), < 75MB RAM, frozen `ThemeBrushes`, diagnostic flags.
* `dynamic-island-release-packaging` — 52-test verification suite, `win-x64` single-file publish, Inno Setup installer.
