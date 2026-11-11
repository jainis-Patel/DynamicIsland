---
name: dynamic-island-live-activities-ipc
description: >-
  Use this skill when integrating external scripts, CLI utilities, background services,
  or AI coding agents (Antigravity, Claude Code, GitHub Copilot) with Dynamic Island's
  Live Activity Hub. Covers the Named Pipe IPC protocol (\\.\pipe\DynamicIsland.Activities),
  JSON message formats, priority queuing, and split-pill secondary droplet rendering.
---

# Dynamic Island Live Activities & IPC Protocol

Dynamic Island provides a high-throughput, low-latency Inter-Process Communication (IPC) endpoint via Windows Named Pipes. Any external terminal command, Python/PowerShell script, AI coding agent, or build runner can broadcast real-time status, progress bars, and alerts directly into the island with sub-millisecond overhead.

---

## Architecture Overview

```
┌─────────────────────────────────┐
│ External App / Script / Agent   │
│ (PowerShell, CLI, Python, etc.) │
└───────────────┬─────────────────┘
                │ (JSON over Named Pipe)
                ▼
┌─────────────────────────────────┐
│ \\.\pipe\DynamicIsland.Activities│ (CurrentUserOnly ACL)
└───────────────┬─────────────────┘
                ▼
┌─────────────────────────────────┐
│ Services/LiveActivityIpcService │ (Non-blocking async reader)
└───────────────┬─────────────────┘
                ▼
┌─────────────────────────────────┐
│     Core/LiveActivityHub.cs     │ (Priority-sorted queue: 0 - 100)
└───────────────┬─────────────────┘
                ▼
┌─────────────────────────────────┐
│     IslandWindow Split Pill     │ (Primary pill + secondary compact droplet)
└─────────────────────────────────┘
```

---

## Named Pipe Endpoint

* **Pipe Name**: `\\.\pipe\DynamicIsland.Activities`
* **Security**: `PipeOptions.CurrentUserOnly` (prevents cross-user elevation attacks).
* **Format**: Single-line UTF-8 JSON request, followed by single-line JSON response.

---

## JSON Wire Protocol

### 1. Publish or Update an Activity
```json
{
  "action": "publish",
  "id": "agent_build",
  "source": "Antigravity",
  "title": "dotnet publish",
  "subtitle": "Building native win-x64 binary...",
  "glyph": "\uE7C3",
  "accent": "#0A84FF",
  "progress": 0.45,
  "priority": 80,
  "targetView": "systemhud"
}
```

* `id` *(string, required)*: Unique identifier for this activity session.
* `source` *(string)*: Display name of the sending tool or agent.
* `title` *(string)*: Bold headline text.
* `subtitle` *(string)*: Detail description text.
* `glyph` *(string)*: Segoe Fluent Icons unicode character (e.g. `\uE7C3` = wrench/hammer, `\uE8B7` = package).
* `accent` *(string)*: Hex color code (e.g. `#0A84FF`, `#30D158`, `#FF9F0A`).
* `progress` *(number, optional)*: `0.0` to `1.0` for progress ring/bar; `null` for indeterminate pulse.
* `priority` *(int, 0–100)*: Determines order in `LiveActivityHub`. Higher values preempt lower values.
* `targetView` *(string, optional)*: View key to open when user clicks the island (e.g. `"shelf"`, `"systemhud"`).

### 2. Update Progress Only (Lightweight)
```json
{
  "action": "update",
  "id": "agent_build",
  "progress": 0.82,
  "subtitle": "Generating single-file bundle..."
}
```

### 3. Complete an Activity
```json
{
  "action": "complete",
  "id": "agent_build",
  "title": "Build Succeeded",
  "subtitle": "DynamicIsland.exe ready"
}
```
* Shows success state briefly, then removes the item from the priority queue.

### 4. Dismiss Immediately
```json
{
  "action": "dismiss",
  "id": "agent_build"
}
```

---

## Client Integration Examples

### PowerShell
```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "DynamicIsland.Activities", [System.IO.Pipes.PipeDirection]::InOut)
$pipe.Connect(1000)

$writer = New-Object System.IO.StreamWriter($pipe, [System.Text.Encoding]::UTF8)
$reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)

$payload = @{
    action = "publish"
    id = "ps_backup"
    source = "PowerShell"
    title = "Database Backup"
    subtitle = "Exporting records..."
    glyph = "`u{E8B7}"
    accent = "#30D158"
    progress = 0.50
    priority = 75
} | ConvertTo-Json -Compress

$writer.WriteLine($payload)
$writer.Flush()
$response = $reader.ReadLine()
$pipe.Close()
```

### Python
```python
import json

with open(r'\\.\pipe\DynamicIsland.Activities', 'r+b', buffering=0) as pipe:
    msg = {
        "action": "publish",
        "id": "py_task",
        "source": "AI Agent",
        "title": "Code Optimization",
        "subtitle": "Analyzing memory leaks...",
        "progress": 0.35,
        "priority": 80
    }
    pipe.write((json.dumps(msg) + "\n").encode('utf-8'))
    response = pipe.readline().decode('utf-8')
```
