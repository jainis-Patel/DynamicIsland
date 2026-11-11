# Liquid Glass Tuning & Parameter Reference Guide

This guide details every physical, optical, and lighting parameter in the GPU Liquid Glass rendering engine, explains their visual effects, and provides tuning recipes for different surface types across Dynamic Island.

---

## 1. Parameter Reference

| Parameter | Type | Default | Clamped Range | Shader Variable | Visual & Physical Effect |
|---|---|---|---|---|---|
| **`Thickness`** | `float` | `18.0` | `0.0` .. `100.0` | `Thickness` | **Physical Glass Thickness (mm/px)**.<br>Scales Snell's refraction ray offset $\Delta \mathbf{p} = \frac{\mathbf{R}_{xy}}{-\mathbf{R}_z} \cdot \text{Thickness} \cdot \text{DpiScale}$. Higher values increase the optical displacement, bending the background more dramatically. Set lower (8–14) for large cards to avoid distracting warping; set higher (18–32) for small pills and buttons for lens-like optical magnification. |
| **`BevelWidth`** | `float` | `22.0` | `1.0` .. `100.0` | `BevelWidth` | **Lens Bevel Rim Depth (px)**.<br>Controls how far inward from the silhouette edge the 3D curvature extends. Within this width, the normal transitions from outward-slanted ($a = 1.0$) to flat lens ($a = 0.0$). A small bevel creates a crisp, razor-sharp edge highlight. A wide bevel creates a soft, convex pebble or water-droplet appearance. |
| **`IndexOfRefraction`** | `float` | `1.50` | `1.001` .. `3.0` | `IndexOfRefraction` | **Index of Refraction (IOR)**.<br>Physical refractive index of the medium ($\eta = 1/\text{ior}$ in Snell's law):<br>- `1.00`: Air (no refraction)<br>- `1.33`: Water<br>- `1.49` – `1.52`: Crown Glass / Acrylic (recommended)<br>- `1.62`: Heavy Flint Glass<br>- `2.42`: Diamond (extreme distortion)<br>Values above 1.7 create intense prismatic warping. |
| **`BlurRadius`** | `float` | `20.0` | `0.0` .. `100.0` | `BlurRadius` | **Separable Gaussian Blur Radius (px)**.<br>Controls the diffusion of the refracted backdrop sampled underneath the glass. Blended continuously between the sharp backdrop and the downscaled Gaussian blur chain: `blurWeight = saturate(BlurRadius / 25.0)`. Set to 0 for a magnifying lens effect; set to 20–35 for Frosted Apple Liquid Glass. |
| **`Saturation`** | `float` | `1.20` | `0.0` .. `3.0` | `Saturation` | **Color Vibrancy Boost**.<br>Interpolates between grayscale luminance and raw RGB in linear space: `lerp(luma, col, sat)`. A value of 1.2–1.4 compensates for transmission luminance loss and makes refracted desktop elements pop with vibrant color. |
| **`Brightness`** | `float` | `1.05` | `0.0` .. `3.0` | `Brightness` | **Transmission Luminance Multiplier**.<br>Scales the linear RGB values of the refracted backdrop. Set to 1.0–1.1 for standard glass; reduce to 0.7–0.85 for smoked dark glass. |
| **`TintColor`** | `Color` | `#600A0A10` | `Color.FromArgb(a, r, g, b)` | `TintColor` | **Absorption / Surface Tint**.<br>Blends on top of the refracted backdrop in linear color space: `lerp(col, Tint.rgb, Tint.a)`. Use a subtle dark tint (`#50000000`) for high-contrast dark mode readability, or a pure white tint (`#20FFFFFF`) for frosty crystal glass. |
| **`SpecularStrength`** | `float` | `0.85` | `0.0` .. `5.0` | `SpecularStrength` | **Fresnel Edge Rim Intensity**.<br>Scales the directional specular highlight along the perimeter. A value around 0.8–1.2 produces a crisp, Apple HIG-style edge sheen. |
| **`LightDirection`** | `Vector2` | `(-0.58, -0.82)` | Unit vector | `LightDirection` | **Virtual Directional Light Vector**.<br>Defines the 2D direction of the primary light source for specular edge lighting. Default is top-left illumination ($\approx -45^\circ$). |
| **`RimPower`** | `float` | `2.5` | `0.2` .. `10.0` | `RimPower` | **Specular Falloff Exponent**.<br>Exponent in $a^{\text{RimPower}}$. Higher values (3.0–5.0) concentrate the specular rim to a needle-thin boundary edge. Lower values (1.5–2.0) spread the shine inward across the bevel. |
| **`Dispersion`** | `float` | `0.0` | `0.0` .. `1.0` | `Dispersion` | **Chromatic Dispersion (Prism Effect)**.<br>Simulates wavelength-dependent refraction by perturbing IOR for red ($\text{ior} \cdot (1 - \delta)$) and blue ($\text{ior} \cdot (1 + \delta)$). Default is `0.0` (off) for pure clarity. Set to `0.10` – `0.25` for subtle prismatic jewel-like color fringing along the bevel rim. |
| **`EdgeFeather`** | `float` | `1.0` | `0.5` .. `4.0` | `EdgeFeather` | **Anti-Aliased Silhouette Feather (px)**.<br>Width of the smoothstep edge anti-aliasing transition: `smoothstep(feather, -feather, d)`. Guarantees zero pixel staircasing on high-DPI displays without MSAA cost. |
| **`CornerRadius`** | `float` | `18.0` | $\ge 0.0$ | `CornerRadius` | **SDF Corner Fillet Radius (px)**.<br>Curvature radius of the rounded rectangle corners. |
| **`CornerSmoothing`** | `float` | `0.80` | `0.0` .. `1.0` | `CornerSmoothing` | **Squircle G2 Continuity Factor**.<br>0.0 gives a standard circular fillet (G1 continuous). 0.8–1.0 produces an Apple-style continuous curvature squircle (G2 continuous), eliminating harsh tangent transitions. |
| **`Shape`** | `Enum` | `RoundedRect` | `RoundedRect`, `Squircle`, `Capsule`, `Circle` | `ShapeType` | **Geometric SDF Primitive**.<br>Selects the analytic distance field equation evaluated per pixel. |
| **`MergeSmoothness`**| `float` | `16.0` | $\ge 0.0$ | `MergeSmoothness` | **Polynomial $s\min$ Liquid Blend Radius (px)**.<br>Smoothing parameter $k$ for merging adjacent glass bodies. When satellite droplets approach the main pill, this controls the width and elasticity of the connecting liquid bridge. |

---

## 2. Tuning Recipes & Preset Profiles

Dynamic Island provides seven pre-calibrated profiles in `GlassPresets`:

### 1. `GlassPresets.Regular` (Standard Apple Liquid Glass)
Balanced all-around preset for the island pill and elevated controls:
```csharp
new GlassSettings.Builder()
    .WithThickness(18.0f)
    .WithBevelWidth(22.0f)
    .WithIndexOfRefraction(1.50f)
    .WithBlurRadius(20.0f)
    .WithSaturation(1.20f)
    .WithBrightness(1.05f)
    .WithTint(Color.FromArgb(0x60, 0x0A, 0x0A, 0x10))
    .WithSpecular(0.85f, rimPower: 2.5f)
    .WithCornerSmoothing(0.80f)
    .Build();
```
*Visual character:* Crystal clear with gentle optical edge bending, rich backdrop saturation, and a subtle obsidian floor tint for readable white typography.

### 2. `GlassPresets.Clear` (Ultra-Transparent Optical Lens)
For preview widgets, image loupes, or camera viewfinders:
```csharp
new GlassSettings.Builder()
    .WithThickness(24.0f)
    .WithBevelWidth(18.0f)
    .WithIndexOfRefraction(1.54f)
    .WithBlurRadius(8.0f)
    .WithSaturation(1.25f)
    .WithBrightness(1.10f)
    .WithTint(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF))
    .WithSpecular(1.10f, rimPower: 2.8f)
    .WithDispersion(0.12f)
    .Build();
```
*Visual character:* High clarity, pronounced edge refraction, delicate chromatic dispersion fringes, and minimal blur.

### 3. `GlassPresets.Dark` (Smoked Obsidian Glass)
For dark-mode popups, clipboard history, notes, and drop shelves:
```csharp
new GlassSettings.Builder()
    .WithThickness(14.0f)
    .WithBevelWidth(20.0f)
    .WithIndexOfRefraction(1.48f)
    .WithBlurRadius(28.0f)
    .WithSaturation(1.10f)
    .WithBrightness(0.85f)
    .WithTint(Color.FromArgb(0x95, 0x05, 0x05, 0x08))
    .WithSpecular(0.70f, rimPower: 2.4f)
    .Build();
```
*Visual character:* Deep, luxurious smoked acrylic. Dampens background noise and guarantees WCAG AAA contrast for light text and colorful badges.

### 4. `GlassPresets.Tinted` (Vibrant Tinted Glass)
For media playback with dynamic album art color extraction:
```csharp
new GlassSettings.Builder()
    .WithThickness(16.0f)
    .WithBevelWidth(20.0f)
    .WithIndexOfRefraction(1.50f)
    .WithBlurRadius(22.0f)
    .WithSaturation(1.35f)
    .WithBrightness(1.00f)
    .WithTint(Color.FromArgb(0x40, 0x20, 0x30, 0x50))
    .WithSpecular(0.90f, rimPower: 2.5f)
    .Build();
```
*Visual character:* Vivid color infusion that blends dynamically with album artwork or app accent colors.

### 5. `GlassPresets.CapsuleButton` (Interactive Action Pill)
For buttons, filter chips, and interactive controls:
```csharp
new GlassSettings.Builder()
    .WithShape(GlassShapeType.Capsule)
    .WithThickness(22.0f)
    .WithBevelWidth(14.0f)
    .WithIndexOfRefraction(1.52f)
    .WithBlurRadius(16.0f)
    .WithSaturation(1.20f)
    .WithBrightness(1.10f)
    .WithTint(Color.FromArgb(0x35, 0x1A, 0x1A, 0x24))
    .WithSpecular(1.25f, rimPower: 3.0f)
    .Build();
```
*Visual character:* Punchy convex bevel with a sharp specular rim. Catches the eye when hovered and provides clear physical affordance.

### 6. `GlassPresets.Pill` (Dynamic Island Main Body)
Engineered for rapid spring morphing between compact and expanded states:
```csharp
new GlassSettings.Builder()
    .WithThickness(18.0f)
    .WithBevelWidth(20.0f)
    .WithIndexOfRefraction(1.50f)
    .WithBlurRadius(20.0f)
    .WithSaturation(1.25f)
    .WithBrightness(1.05f)
    .WithTint(Color.FromArgb(0x75, 0x08, 0x08, 0x0C))
    .WithSpecular(0.95f, rimPower: 2.6f)
    .WithMergeSmoothness(18.0f)
    .WithCornerSmoothing(0.85f)
    .Build();
```

### 7. `GlassPresets.Card` (Expanded Views & Flyouts)
For large surfaces (Expanded Calendar, System Performance, Weather, LauncherView):
```csharp
new GlassSettings.Builder()
    .WithThickness(12.0f)
    .WithBevelWidth(24.0f)
    .WithIndexOfRefraction(1.47f)
    .WithBlurRadius(32.0f)
    .WithSaturation(1.15f)
    .WithBrightness(0.90f)
    .WithTint(Color.FromArgb(0x80, 0x0A, 0x0A, 0x12))
    .WithSpecular(0.65f, rimPower: 2.2f)
    .WithCornerRadius(24.0f)
    .WithCornerSmoothing(0.75f)
    .Build();
```
*Visual character:* Restrained refraction at the borders so internal data grids, charts, and lists remain stable and distortion-free.

---

## 3. Performance & Quality Level Tuning

`GlassManager.Instance.Quality` provides four runtime performance tiers:

| Quality Tier | Downscale | Separable Blur Passes | Chromatic Dispersion | Target Hardware |
|---|---|---|---|---|
| **`GlassQuality.High`** | Full / Half | 2 passes ($r \le 32$) | Enabled (3 refraction taps) | Dedicated GPUs, modern Intel Iris Xe / AMD Radeon 680M+ |
| **`GlassQuality.Medium`** | $2\times$ Downscale | 2 passes ($r \le 20$) | Disabled (1 refraction tap) | Standard Intel UHD Graphics 620/630, budget laptops |
| **`GlassQuality.Low`** | $4\times$ Downscale | 1 box pass | Disabled | Battery Saver mode, legacy integrated graphics |
| **`GlassQuality.Off`** | N/A | Off | Off | Windows "Transparency Effects" disabled in OS Settings |

### OS Transparency & Motion Integration
When Windows "Transparency Effects" are disabled in Settings (`SystemParameters.DropShadow` or DwmGetWindowAttribute), `GlassManager` cleanly falls back to opaque frozen OLED brushes (`ThemeBrushes.PillBackgroundDark`), avoiding unnecessary GPU overhead.

---

## 4. Live Interactive Tuning with GlassPlayground

You can test and tune any combination of parameters in real time with the built-in interactive playground:

```powershell
DynamicIsland.exe --playground
# or
DynamicIsland.exe --glass-playground
```

Features:
- Live sliders for all 16 `GlassSettings` fields.
- Real-time toggle between **Static Testbench** (checkerboard, high-contrast stripes, text) and **Live Desktop Capture** (Path B).
- Preset dropdown (`Regular`, `Clear`, `Dark`, `Tinted`, `CapsuleButton`, `Pill`, `Card`).
- Interactive dragging of the main glass shape.
- Secondary satellite droplet with live slider to demonstrate the $s\min$ fluid meniscus bridge in real time.
