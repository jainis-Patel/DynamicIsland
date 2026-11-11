# Dynamic Island Widget Conventions & XAML Design System

This reference outlines the visual styling rules, squircle layout dimensions, color tokens, and resource definitions used across all Dynamic Island views in `IslandWindow.xaml` and `Resources/IslandStyles.xaml`.

---

## 1. Dimensional Standards

When expanding the pill into your widget, choose standard dimension presets defined in `IslandWindow.Widgets.cs` -> `GetTargetShape()`:

| View Type | Width | Height | Corner Radius | Description |
|---|---|---|---|---|
| **Compact Utility** | 496 px | 192 px | 36 px | Clock, Mini Weather, Pomodoro, Network snapshot |
| **Standard Card** | 496 px | 240 px | 38 px | Media Controls, Clipboard, Audio Output Switcher |
| **Extended Surface** | 496 px | 338 px | 40 px | Settings, System Performance HUD, Hardware Monitor |
| **Drop Shelf** | 496 px | 220 px | 38 px | File staging cards with thumbnail grids |

---

## 2. Color Palette & Frozen Brushes

Always bind to dynamic application resources defined in `Resources/IslandStyles.xaml` or use `Core.ThemeBrushes` to guarantee frozen render thread safety:

```xml
Background="{DynamicResource CardSurfaceBrush}"  <!-- #121216 (Apple Dark Glass) -->
BorderBrush="{DynamicResource CardBorderBrush}"  <!-- #1AFFFFFF (10% translucent white outline) -->
Foreground="{DynamicResource SecondaryBrush}"    <!-- #86868B (Apple secondary muted text) -->
Foreground="{DynamicResource AccentBrush}"       <!-- #FF9F0A (Apple system orange) -->
```

* **Never create unfrozen brushes in code-behind:** Always call `.Freeze()` on brushes or use `ThemeBrushes.DarkGray`, `ThemeBrushes.White`, etc.

---

## 3. Typography & Glyph Standards

* **Glyph Font:** Segoe Fluent Icons (`FontFamily="{StaticResource IconFont}"`).
* **Header Typography:** `FontSize="13"`, `FontWeight="SemiBold"`, `Foreground="#FFFFFF"`.
* **Subtitle Typography:** `FontSize="11"`, `FontWeight="Regular"`, `Foreground="{StaticResource SecondaryBrush}"`.
* **Monospace Numbers / Telemetry:** Use `FontFamily="Consolas, Segoe UI"` with `TextOptions.TextRenderingMode="ClearType"`.

---

## 4. Squircle & Card Layout Template

Wrap all sub-view elements in standard squircle border cards:

```xml
<Border Background="{DynamicResource CardSurfaceBrush}"
        BorderBrush="{DynamicResource CardBorderBrush}"
        BorderThickness="1"
        CornerRadius="16"
        Padding="14,12"
        Margin="12,8">
    <Grid>
        <!-- Card Content -->
    </Grid>
</Border>
```

---

## 5. Dock Strip Button Convention

When adding a button to `DockIconsStack` in `IslandWindow.xaml`:

```xml
<Button x:Name="DockBtnMyWidget"
        Style="{StaticResource DockIconButton}"
        ToolTip="My Widget Name"
        Click="OnDockMyWidgetClick">
    <TextBlock Text="&#xE713;"
               FontFamily="{StaticResource IconFont}"
               FontSize="13" />
</Button>
```

In `IslandWindow.Widgets.cs`:
```csharp
private void OnDockMyWidgetClick(object sender, RoutedEventArgs e)
{
    SwitchExpandedView(ExpandedView.MyWidget);
}
```
