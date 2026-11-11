# Dynamic Island for Windows

Created with ❤️ by **Jainis Patel**

An interactive, Apple-inspired **Dynamic Island** desktop overlay for Windows 10 & 11 built with **WPF (.NET 8)**, closed-form analytical spring physics, WASAPI real-time audio spectrum analysis, and WinRT system integrations.

---

## Key Features

- **Analytical Spring Morphing (`60 Hz – 240 Hz+`)**: Frame-rate-independent closed-form damped harmonic oscillator (`Core/Spring.cs`) driving pill width, height, and corner radius across 5 states (`Hidden > Expanded > Toast > Compact > Idle`), with automatic critically-damped fallback when Windows Reduced Motion is active.
- **Now Playing & Live Audio Equalizer**:
  - Connects to `GlobalSystemMediaTransportControlsSessionManager` (Spotify, Chrome, Edge, Firefox, VLC, Windows Media Player).
  - Extracts vibrant album-artwork accent colors (`Services/ColorExtractor.cs`) and drives a 4-band WASAPI loopback FFT equalizer (`Services/AudioSpectrum.cs`) plus an ambient bass-reactive backlight halo.
  - Click the media source badge in the expanded view to cycle between multiple active media players, or `Shift + Scroll` / tilt-scroll on the pill to skip tracks.
- **Priority-Aware Toast Queue & Action Center Notifications**:
  - Converts incoming Windows notifications into Dynamic Island banners (`Services/NotificationListenerService.cs`).
  - Polls `UserNotificationListener.GetNotificationsAsync` (the `NotificationChanged` event only fires for packaged MSIX apps). Requires **Settings › Privacy & security › Notifications › Let apps access your notifications**.
  - **Show Notifications in Island Only** (tray menu, on by default) dismisses the native Windows banner after it appears in the island. Note that this also removes it from Action Center.
  - Priority queue (`Critical > Notification > System > Feedback > Low`) with in-place category coalescing so volume scroll steps never overwrite incoming messages or low-battery warnings.
  - Built-in **Do Not Disturb (DND)** toggle and automatic Windows Focus Assist (`QUNS_QUIET_TIME`) suppression.
- **Privacy-First Clipboard Manager**:
  - Tracks copied text snippets, URLs, hex color swatches, and downscaled/frozen screenshot thumbnails (`Services/ClipboardHistoryService.cs`).
  - Automatically excludes password-manager secrets (`ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory`).
- **Drop Shelf ("Drop Notch") & Live Download Tracker**:
  - Drag and drop files onto the pill to hold them for quick drag-out later (`Services/DropShelfService.cs`), and automatically surface completed browser downloads (`Services/DownloadTrackerService.cs`).
- **Stopwatch & 25-Minute Pomodoro Countdown**:
  - Built-in count-up stopwatch and 1-click **25m Pomodoro** countdown timer (`Services/CountdownTimerService.cs`) with live Compact-mode display.
- **Inline Calculator & Unit Converter**:
  - Evaluates arithmetic, exponents, square roots (`sqrt(144)`), percentages (`20% of 450`), temperatures, lengths, weights, digital storage (`2 gb in mb`), and currencies (`Services/QuickCalcService.cs`).
- **Hardware Privacy Dots & 1-Click Mic Mute**:
  - Kernel-level registry watcher (`RegNotifyChangeKeyValue`) for zero-CPU webcam & microphone active indicators, call duration timer, and hardware microphone mute (`Services/PrivacyIndicatorService.cs`).
- **Audio Output Switcher, Smart Power & Wireless Peripherals Hub**:
  - 1-click default audio output switching via `IPolicyConfig` (`Services/AudioEndpointService.cs`), Windows Power Scheme switching (`Services/PowerPlanService.cs`), and Bluetooth / XInput controller battery telemetry (`Services/PeripheralDeviceService.cs`).

---

## Global Hotkeys

All hotkeys use `Ctrl + Alt + <Key>` by default and can be customized or disabled via `CustomHotkeys` in `%LocalAppData%\DynamicIsland\settings.json`:

| Shortcut | Action |
|---|---|
| `Ctrl + Alt + Space` | Play / Pause active media session |
| `Ctrl + Alt + I` | Toggle Expand / Collapse Dynamic Island |
| `Ctrl + Alt + A` | Start / Pause Stopwatch or Pomodoro timer |
| `Ctrl + Alt + V` | Open Clipboard History |
| `Ctrl + Alt + C` | Open Quick Calculator & Unit Converter |
| `Ctrl + Alt + O` | Open Audio Output Switcher |
| `Ctrl + Alt + P` | Open Battery & Power Mode widget |
| `Ctrl + Alt + H` | Open System Performance & Activity HUD |
| `Ctrl + Alt + R` | Start / Stop Voice Memo recording |
| `Ctrl + Alt + D` | Open Connected Peripherals Battery Hub |
| `Ctrl + Alt + W` | Open Wi-Fi & Network Hub |
| `Ctrl + Alt + N` | Open Notification History Center |
| `Ctrl + Alt + S` | Open Appearance & Settings |
| `Ctrl + Alt + M` | Switch Target Monitor |
| `Ctrl + Alt + Q` | Exit Dynamic Island |

---

## Build, Test & Run

### Prerequisites
- Windows 10 (build 19041+) or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build & Run
```powershell
dotnet build -c Release
dotnet run -c Release
```

### Run the Built-In Verification Suite
```powershell
dotnet run -c Release -- --self-test
```

### Publish Single-File Executable
```powershell
dotnet publish -c Release -p:PublishProfile=WinX64
```

---

## Installation & Setup

Dynamic Island provides multiple zero-configuration installation options with no administrator UAC elevation required:

### Option 1: Native Dark-Mode Setup Wizard (Recommended)
Double-click `installer\DynamicIsland-Setup.exe` (or `publish\DynamicIsland-Setup.exe`).
- Custom dark-mode installation wizard with squircle controls.
- Configurable install directory (defaults to `%LocalAppData%\Programs\DynamicIsland`).
- Automatic Start Menu & Desktop shortcut creation.
- Seamless Windows Startup autostart configuration.
- Full Windows **Installed Apps** (Add/Remove Programs) registration.
- Silent command-line install: `DynamicIsland-Setup.exe --install --silent`.

### Option 2: 1-Click Batch / PowerShell Script
- Double-click `installer\Install.bat` to launch the automated script installer.
- Or run PowerShell directly:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\installer\Install.ps1
  ```

### Option 3: Inno Setup
- Compile `installer\DynamicIsland.iss` using Inno Setup 6 (`iscc.exe DynamicIsland.iss`).

### Uninstallation
- Open Windows **Settings › Apps › Installed apps**, search for **Dynamic Island**, and click **Uninstall** (launches the custom uninstallation wizard).
- Or run `installer\Uninstall.bat` / `powershell -ExecutionPolicy Bypass -File .\installer\Uninstall.ps1`.

---

## Data & Persistence

All user data is stored atomically in `%LocalAppData%\DynamicIsland\`:
- `settings.json` — Theme, dock alignment, notch offset, target monitor device name, DND state, and custom hotkeys.
- `clipboard.json` — Recent non-sensitive text/URL/color clipboard history.
- `shelf.json` — Pinned Drop Shelf file paths.
- `app.log` / `app.1.log` — Asynchronous rotating diagnostic logs (capped at 512 KB per file).
