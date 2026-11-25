---
name: dynamic-island-release-packaging
description: >-
  Use this skill when building, self-testing, publishing, or creating deployment installers
  for Dynamic Island. Covers running the deterministic 86-test verification suite, single-file
  native publishing (win-x64), Inno Setup packaging, dark-mode SetupWindow/UninstallWindow
  wizards, Windows startup autostart integration, and firewall helper scripts.
---

# Dynamic Island Build, Verification & Release Runbook

Windows customization communities on Reddit place heavy emphasis on clean, portable, and transparent distribution:
* **No Admin Elevation Required**: Installs into `%LocalAppData%\Programs\DynamicIsland` without nagging UAC prompts.
* **Deterministic Verification**: Every release must pass the 86-point automated verification suite.
* **Clean Uninstall Guarantee**: Zero orphaned registry keys or lingering autostart entries (`UninstallWindow.xaml`).
* **Single-File Native Executable**: Zero external framework dependencies for end users.

This runbook documents the complete lifecycle from build to verification and release packaging.

---

## The Build & Verification Pipeline

```
┌─────────────────────────────────┐
│     dotnet build -c Release     │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│  dotnet run -- --self-test      │ ──► [SelfTestRunner.cs: 86 Deterministic Checks]
└───────────────┬─────────────────┘
                │ (All Passed)
                ▼
┌─────────────────────────────────┐
│ dotnet publish (Single-File)    │ ──► DynamicIsland.exe (win-x64, self-contained)
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ Release Installer Packaging     │ ──► Inno Setup (.iss) / SetupWindow.xaml / Install.ps1
└─────────────────────────────────┘
```

---

## 1. Automated Verification Suite (`--self-test`)

Before publishing any code change, execute the deterministic test suite:

```powershell
dotnet run -c Release -- --self-test
```

### What It Verifies (84 Checks):
* **Spring Physics**: Frame-rate invariance across 60Hz, 144Hz, and 240Hz, stalled frame recovery, and liquid intensity damping.
* **State Machine**: Priority hierarchy (`Hidden > Expanded > Toast > Compact > Idle`), toast queues, and category coalescing.
* **Fullscreen Detection**: Borderless & standard taskbar handling.
* **Clipboard Privacy**: 4K downsampling and exclusion of password manager secrets (`ExcludeClipboardContentFromMonitorProcessing`).
* **Persistence**: Atomic JSON storage, schema migrations, and DPAPI encryption round-trips.
* **Audio & Lyrics**: WASAPI lock-free reads, LRCLIB timestamp parsing, and title sanitization.
* **Hardware & Systems**: Multi-monitor coordinate positioning, Wi-Fi SSID parsing, battery telemetry, and zero-allocation CPU/RAM snapshots.
* **Droplets & Productivity**: Droplets registry, Jiggle detector, File converter / PDF merger, LiveActivityHub, and Paste Queue.
* **IPC & Meetings**: Live Activity IPC protocol (`\\.\pipe\DynamicIsland.Activities`), Meeting Mode coordinator, Lock/Keyboard HUDs.
* **Productivity Droplets**: Notes scratchpad, Quick Launcher, Termi-Notch, Window Snap, and Mechanical Key Sounds.
* **Sharing & Phone Bridge**: Windows Share Sheet interop, Phone Bridge LAN QuickShare HTTP server, and QR Code generator.
* **Navigation & Focus**: Expanded view focusability, input traps, and key roundtrips.

---

## 2. Publish Single-File Executable

To generate the standalone executable for distribution:

```powershell
dotnet publish -c Release -p:PublishProfile=WinX64
```

Or directly via CLI parameters:
```powershell
dotnet publish DynamicIsland.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\
```

The output binary in `publish\DynamicIsland.exe` is completely self-contained and ready to execute on Windows 10 (19041+) and Windows 11.

---

## 3. Installer Packaging Options

### A. Native Inno Setup Installer (`installer/DynamicIsland.iss`)
Compile `installer/DynamicIsland.iss` using Inno Setup 6:
* Installs cleanly to `%LocalAppData%\Programs\DynamicIsland` (per-user, non-admin).
* Packages `DynamicIsland.exe`, `app.ico`, and `AllowPhoneBridgeFirewall.bat`.
* Creates Start Menu and optional Desktop shortcuts.
* Registers Windows "Installed Apps" entry pointing to `DynamicIsland.exe --uninstall`.

### B. PowerShell Scripted Installer (`installer/Install.ps1`)
Can be run on clean Windows machines or CI:
```powershell
powershell -ExecutionPolicy Bypass -File installer/Install.ps1
```
* Supports `-NoAutoStart`, `-NoDesktopShortcut`, and `-NoLaunch`.

### C. Dark-Mode Setup Wizard (`DynamicIsland.exe --setup`)
Dynamic Island embeds its own native WPF setup wizard in `Views/SetupWindow.xaml`:
* Fluent dark-mode UI with squircle checkboxes and progress bar.
* Allows user to select desktop shortcuts, startup launch, and autostart immediately.

---

## 4. Post-Install Verification & Diagnostics

Run the performance and memory health check:
```powershell
dotnet run -c Release -- --diagnose-memory
```
Ensure working set is < 75 MB and idle CPU is < 0.1%.
