---
name: dynamic-island-widget-creator
description: >-
  Use this skill when designing, scaffolding, or adding new widgets or modular UserControls
  to Dynamic Island (such as synced lyrics, launcher, termi-notch, calendar/tasks, notes,
  or hardware monitors). Provides the end-to-end architecture, XAML styling patterns,
  Views/Widgets UserControls, ExpandedView & DropletRegistry registration, IslandWindow.Navigation.cs
  routing, ServiceContainer lifecycle, and zero-idle-CPU rules.
---

# Dynamic Island Widget Creator

This runbook guides you through creating, styling, and integrating new widgets into the Dynamic Island desktop overlay for Windows.

Dynamic Island uses a fully modular UserControl architecture. Each widget lives in its own independent XAML/C# component under `Views/Widgets/`, wires into `IslandWindow.Navigation.cs` and `IslandWindow.ViewForwarders.cs`, and (if togglable) registers with `Core/DropletRegistry.cs` to ensure a **strict zero-idle-CPU footprint** (< 0.1% CPU, 0 RAM overhead when collapsed or disabled).

---

## Modern Widget Architecture Overview

Adding a widget to Dynamic Island connects the following modular subsystems:

```
┌────────────────────────┐      ┌──────────────────────────┐
│ Core/ExpandedView.cs   │ ◄─── │ Services/MyWidgetService │ (IDisposable, events)
└───────────┬────────────┘      └────────────┬─────────────┘
            │                                │
            ▼                                ▼
┌────────────────────────┐      ┌──────────────────────────┐
│ Views/Widgets/         │ ◄─── │ Core/DropletRegistry.cs  │ (Optional modular droplet toggle)
│ MyWidgetView.xaml(.cs) │      └──────────────────────────┘
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐      ┌──────────────────────────┐
│ IslandWindow.xaml      │ ◄─── │ Core/ServiceContainer.cs │ (Managed reverse lifecycle)
└───────────┬────────────┘      └──────────────────────────┘
            │
            ▼
┌────────────────────────────────────────────────────────┐
│ IslandWindow.Navigation.cs & ViewForwarders.cs        │ (Morph shape, dock button, registry)
└────────────────────────────────────────────────────────┘
```

---

## Step-by-Step Implementation Procedure

### 1. Register Strong View Type in `Core/ExpandedView.cs`

1. Open [ExpandedView.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Core/ExpandedView.cs).
2. Add your widget to the `ExpandedView` enum:
   ```csharp
   public enum ExpandedView
   {
       Media,
       Shelf,
       Clipboard,
       // ... existing views ...
       MyWidget
   }
   ```
3. Update `ToKey()` and `TryParseKey()` extension methods:
   ```csharp
   ExpandedView.MyWidget => "mywidget",
   ```

### 2. Scaffold the Backend Service in `Services/`

Create `Services/MyWidgetService.cs`:
* **Implement `IDisposable`**: Clean up any Windows API hooks, `DispatcherTimer`, or event subscriptions.
* **Zero-Idle-CPU Principle**: Do NOT poll while the widget is collapsed. Implement an `Active` or `SetExpanded(bool isExpanded)` property so background polling or render loops only tick when the view is actually visible to the user.
* **Local Atomic Persistence**: If persisting user data, use `Core.AtomicJsonStore` targeting `Core.AppPaths.GetDataFilePath("mywidget.json")`. Never write directly with unbuffered `File.WriteAllText`.

### 3. Register in `Core/ServiceContainer.cs` and `App.xaml.cs`

1. Open `Core/ServiceContainer.cs`.
2. Register the service instance:
   ```csharp
   public MyWidgetService? MyWidget { get; set; }
   ```
3. In `Dispose()`, ensure reverse-order deterministic disposal.

### 4. Create the Modular View in `Views/Widgets/MyWidgetView.xaml`

Create `Views/Widgets/MyWidgetView.xaml` and `.xaml.cs` as a WPF `UserControl`:
* Use Apple-inspired dark glass styling:
  * CornerRadius: 16–22
  * Background: `#E0000000` / `#0DFFFFFF` (or `ThemeBrushes.CardBackground`)
  * Border: `ThemeBrushes.CardBorder`
  * Typography: `Segoe UI Variable Display` or `Segoe UI Variable Text`
* Expose public internal properties for controls so `IslandWindow.ViewForwarders.cs` can bridge them cleanly if needed.

### 5. Mount UserControl in `IslandWindow.xaml`

Inside `ExpandedViewContainer` (`<Grid x:Name="ExpandedViewContainer" ...>`):
```xml
<widgets:MyWidgetView x:Name="MyWidgetContainer"
                      Visibility="Collapsed"
                      Opacity="0"
                      SnapsToDevicePixels="True" />
```
Add its navigation icon to the Dock strip (`<StackPanel x:Name="DockIconsStack" ...>`) with a Segoe Fluent Icons glyph.

### 6. Wire Navigation & Morph Dimensions in `IslandWindow.Navigation.cs`

1. Open [IslandWindow.Navigation.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/IslandWindow.Navigation.cs).
2. Register in `_viewRegistry`:
   ```csharp
   _viewRegistry[ExpandedView.MyWidget] = (MyWidgetContainer, DockBtnMyWidget);
   ```
3. Define the morph shape in `IslandWindow.xaml.cs` (e.g. `PillShape MyWidgetExpandedShape = new(484, 232, 34);`).
4. If the widget requires keyboard text entry (search box, notepad, terminal), add it to `SetKeyboardFocusable()`:
   ```csharp
   view is ExpandedView.MyWidget or ...
   ```
5. Hook view activation/deactivation in `SwitchExpandedView()`:
   ```csharp
   if (view == ExpandedView.MyWidget)
       _myWidgetService?.SetExpanded(true);
   ```

### 7. Optional: Register as a Modular Droplet in `Core/DropletRegistry.cs`

If the feature can be toggled on/off to save RAM/CPU, register a descriptor in `Core/DropletRegistry.cs`:
```csharp
Register(new DropletDescriptor
{
    Id = "my_widget",
    Name = "My Widget Feature",
    Description = "Quick utility description",
    Glyph = "\uE71D",
    Category = DropletCategory.Productivity,
    TargetView = ExpandedView.MyWidget,
    DockPinKey = "mywidget",
    EstimatedRamKb = 48,
    IsDefaultEnabled = true,
    OnActivate = () => _myWidgetService?.SetEnabled(true),
    OnDeactivate = () => _myWidgetService?.SetEnabled(false)
});
```

### 8. Verify Deterministically in `SelfTestRunner.cs`

1. Add a check to [SelfTestRunner.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Core/SelfTestRunner.cs).
2. Run verification:
   `dotnet run -c Release -- --self-test`

---

## Detailed References

* [Widget Styling & Design Conventions](./references/widget-conventions.md)
* [Zero Idle Memory Rules](../dynamic-island-performance-profiler/SKILL.md)
