# Real Liquid Glass Engine: Architecture & Technical Specification

## 1. Overview & Core Philosophy

The Liquid Glass rendering engine in **DynamicIsland** is a physically-grounded, GPU-accelerated Direct3D 11 optical rendering pipeline. It replaces fake, CSS/XAML-style "glassmorphism" (stacked borders, gradient overlays, drop shadows, and uniform blur overlays) with real **per-pixel ray refraction**, **surface normal reconstruction**, **curved lens bevels**, **chromatic dispersion**, and **polynomial smooth-union fluid merging**.

```
+-------------------------------------------------------------------------------------------------+
|                                     Desktop / In-App Backdrop                                   |
|   (Windows.Graphics.Capture or RenderTargetBitmap -> ID3D11ShaderResourceView, Linear RGB)     |
+-------------------------------------------------------------------------------------------------+
                                               |
                                     [Downscale 2x / 4x]
                                               v
                               +-------------------------------+
                               | Separable Gaussian Blur Chain |
                               |  (Ping-Pong D3D11 Render Target)|
                               +-------------------------------+
                                               |
                          +--------------------+--------------------+
                          | (Blurred Texture)                       | (Sharp Texture)
                          v                                         v
+-------------------------------------------------------------------------------------------------+
|                                 GlassShaderCore.hlsl (Pixel Shader)                            |
|                                                                                                 |
|   1. Multi-Shape Signed Distance Field: sdRoundRect, sdSquircle, sdCapsule, sdCircle            |
|   2. Polynomial Smooth-Union (smin): Liquid droplet bridge between adjacent bodies              |
|   3. Analytical Central-Difference Gradient: g = normalize(grad(SDF))                          |
|   4. Convex Bevel Profile & Normal: a = saturate(1 - s/bevel), n = (g * a, sqrt(1 - a^2))       |
|   5. Snell's Law 3D Refraction: R = refract(float3(0,0,-1), n, 1/ior)                           |
|   6. Coordinate Displacement: offset = (R.xy / -R.z) * thickness * DpiScale                     |
|   7. Multi-Tap Spectral Dispersion: Sample Red (1/ior-), Green (1/ior), Blue (1/ior+)           |
|   8. Post-Refraction Color Processing: Saturation, Brightness, Tint Overlay, Linear-to-sRGB    |
|   9. Directional Specular Lighting: Dot(g, L) rim + Ambient counter-rim + Edge feathering       |
+-------------------------------------------------------------------------------------------------+
                                               |
                                               v
+-------------------------------------------------------------------------------------------------+
|                                WPF Presentation & Composition                                   |
|    - Offscreen Direct3D11 Surface / WriteableBitmap Interop                                     |
|    - Zero-Idle-CPU: CompositionTarget.Rendering decoupled when springs are at rest              |
|    - Content Airspace Isolation: Labels, Icons, and Audio Spectrum rendered as top-level XAML   |
+-------------------------------------------------------------------------------------------------+
```

---

## 2. Mathematical Foundation

### 2.1 Signed Distance Fields (SDFs)

The boundary and interior shape of every glass body are defined analytically as a scalar function $d(\mathbf{p})$, where $d(\mathbf{p}) < 0$ inside the glass, $d(\mathbf{p}) = 0$ on the silhouette edge, and $d(\mathbf{p}) > 0$ outside:

1. **Rounded Rectangle**:
   $$\mathbf{q} = |\mathbf{p}| - \mathbf{h} + r$$
   $$d(\mathbf{p}) = \min(\max(q_x, q_y), 0) + \|\max(\mathbf{q}, \mathbf{0})\| - r$$
   where $\mathbf{h}$ is the half-extents vector and $r$ is the corner radius.

2. **Squircle / Continuous Curvature (Superellipse)**:
   Calculated by raising normalized coordinates to exponent $n = 2 + 6 \cdot \text{smoothing}$:
   $$d_{\text{squircle}}(\mathbf{p}) = \left(\left|\frac{p_x}{h_x}\right|^n + \left|\frac{p_y}{h_y}\right|^n\right)^{1/n} - 1$$
   Blended smoothly with the rounded rectangle based on `CornerSmoothing` ($0.0 \le s \le 1.0$) to eliminate curvature discontinuity (G2 continuity).

3. **Capsule**:
   $$\mathbf{ba} = \mathbf{b} - \mathbf{a}, \quad \mathbf{pa} = \mathbf{p} - \mathbf{a}$$
   $$t = \text{clamp}\left(\frac{\mathbf{pa} \cdot \mathbf{ba}}{\mathbf{ba} \cdot \mathbf{ba}}, 0, 1\right)$$
   $$d(\mathbf{p}) = \|\mathbf{pa} - \mathbf{ba} \cdot t\| - r$$

4. **Circle**:
   $$d(\mathbf{p}) = \|\mathbf{p}\| - r$$

### 2.2 Fluid Multi-Body Smooth-Union ($s\min$)

When the Dynamic Island expands, ejects satellite droplets (such as the live activity secondary droplet or drop shelf basket), or retracts, individual glass bodies merge fluidly using polynomial smooth-minimum:

$$h = \text{clamp}\left(0.5 + 0.5 \cdot \frac{d_2 - d_1}{k}, 0.0, 1.0\right)$$
$$s\min(d_1, d_2, k) = \text{lerp}(d_2, d_1, h) - k \cdot h \cdot (1 - h)$$

- When $|d_1 - d_2| \ge k$, $s\min(d_1, d_2, k) = \min(d_1, d_2)$ (no influence at a distance).
- When $d_1 = d_2 = 0$, $s\min(0, 0, k) = -\frac{k}{4}$ (creates an organic meniscus / liquid bridge connecting the two droplets).

### 2.3 Surface Normal Reconstruction

Real glass acts as a 3D convex lens: the outer perimeter has a steep curvature that refracts light aggressively, while the interior is flat:

1. **Outward Gradient Vector**:
   $$\mathbf{g} = \text{normalize}\left(\nabla d(\mathbf{p})\right) = \text{normalize}\left(\frac{d(\mathbf{p} + \epsilon \mathbf{i}) - d(\mathbf{p} - \epsilon \mathbf{i})}{2\epsilon}, \frac{d(\mathbf{p} + \epsilon \mathbf{j}) - d(\mathbf{p} - \epsilon \mathbf{j})}{2\epsilon}\right)$$
2. **Bevel Profile**:
   $$s = \max(-d(\mathbf{p}), 0)$$
   $$a = \text{saturate}\left(1.0 - \frac{s}{\text{BevelWidth}}\right)$$
   $a = 1.0$ at the outer rim; $a = 0.0$ in the flat interior.
3. **Normal Vector ($\mathbf{n}$)**:
   $$n_z = \sqrt{\max(1.0 - a^2, 10^{-4})}$$
   $$\mathbf{n} = \text{normalize}\begin{pmatrix} g_x \cdot a \\ g_y \cdot a \\ n_z \end{pmatrix}$$

### 2.4 Snell's Law Refraction & Screen Displacement

Using the camera ray $\mathbf{I} = (0, 0, -1)$ entering the glass from the eye into the screen:

$$\mathbf{R} = \text{refract}\left(\mathbf{I}, \mathbf{n}, \frac{1}{\text{ior}}\right)$$

Using the refracted ray vector $\mathbf{R}$, the screen-space pixel sampling displacement is:

$$\Delta \mathbf{p} = \left(\frac{R_{xy}}{-R_z}\right) \cdot \text{Thickness} \cdot \text{DpiScale}$$

- In the flat interior ($\mathbf{n} = (0, 0, 1)$), $\mathbf{R} = (0, 0, -1)$, resulting in $\Delta \mathbf{p} = \mathbf{0}$ (crystal-clear flat transmission).
- Along the bevel rim, $\mathbf{R}_{xy}$ curves inward towards the optical center, generating physical optical magnification and edge warping.

### 2.5 Chromatic Dispersion

Physical glass has wavelength-dependent indices of refraction (Cauchy dispersion). When `Dispersion > 0`:

$$\eta_{\text{red}} = \frac{1}{\text{ior} \cdot (1 - \delta)}, \quad \eta_{\text{green}} = \frac{1}{\text{ior}}, \quad \eta_{\text{blue}} = \frac{1}{\text{ior} \cdot (1 + \delta)}$$

where $\delta = \text{Dispersion} \cdot 0.08$. Three separate refraction rays $\mathbf{R}_{\text{red}}, \mathbf{R}_{\text{green}}, \mathbf{R}_{\text{blue}}$ are computed, sampling the backdrop at slightly separated UV coordinates to generate spectral chromatic aberration along the glass perimeter.

### 2.6 Directional Specular Rim Lighting

A directional specular highlight is computed against a virtual light direction $\mathbf{L}$:

$$\text{specular} = a^{\text{RimPower}} \cdot \left(\text{saturate}(\mathbf{g} \cdot \mathbf{L}) + 0.35 \cdot \text{saturate}(-\mathbf{g} \cdot \mathbf{L}) + 0.08\right) \cdot \text{SpecularStrength}$$

- $\text{saturate}(\mathbf{g} \cdot \mathbf{L})$: Primary bright highlight facing the light source.
- $0.35 \cdot \text{saturate}(-\mathbf{g} \cdot \mathbf{L})$: Secondary soft Fresnel bounce on the opposing rim.
- $0.08$: Subtle ambient perimeter glow.

---

## 3. Architecture & Seams

The engine is encapsulated in `src/LiquidGlass/`:

| Component | Responsibility |
|---|---|
| `GlassShaderCore.hlsl` | Single source of truth for vertex & pixel shaders (SDF, $s\min$, Snell's law, normals, dispersion, specular lighting). Compiled at build time via `fxc.exe` into `.cso` bytecode embedded in the assembly. |
| `BlurSeparable.hlsl` | 2-pass horizontal & vertical Gaussian blur shader compiled into `.cso`. |
| `GlassSettings` | Immutable C# record with fluent `Builder` pattern, mathematical parameter clamping, and preset factories. |
| `GlassPresets` | Ready-to-use profiles: `Regular`, `Clear`, `Dark`, `Tinted`, `CapsuleButton`, `Pill`, `Card`. |
| `IBackdropProvider` | Abstraction providing the underlying desktop or window texture: `Task StartAsync()`, `ID3D11ShaderResourceView? CurrentFrameSRV`, `event FrameReady`, `Stop()`. |
| `DesktopCaptureBackdrop` | **Path B:** Uses `Windows.Graphics.Capture` with a free-threaded frame pool. Features `WDA_EXCLUDEFROMCAPTURE` feedback loop prevention, Windows 11 yellow border removal, and zero CPU copies. |
| `VisualBackdrop` | **Path A:** In-app visual capture utilizing `RenderTargetBitmap` with dirty-rect invalidation. |
| `StaticBackdropProvider` | Procedural testbench generating calibrated optical grids, typography, spectrum bars, and diagonal stripes for validation. |
| `GlassRenderer` | Owns Direct3D 11 device, immediate context, shader bytecode, ping-pong blur render targets, constant buffers, and offscreen presentation pipelines. |
| `GlassHost` | Attached XAML properties (`GlassHost.IsGlass="True"`, `GlassHost.Preset="Regular"`) and programmatic `Attach()` / `Detach()` lifecycle manager. |
| `GlassManager` | Application-wide singleton orchestrating backdrop sharing, quality switching (`High`, `Medium`, `Low`, `Off`), and pill spring motion rendering. |
| `GoldenImageGenerator` | Headless verification suite rendering benchmark frames and validating physical refraction displacement. |

---

## 4. Dual Backdrop Strategy (Path B + Path A)

### Path B: Desktop Capture (Primary Overlay Pipeline)
Since Dynamic Island is a top-level desktop overlay window (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED`), its primary job is refracting other desktop windows and wallpapers sitting behind it:

1. **`Windows.Graphics.Capture`**: Captures the target monitor directly into a Direct3D 11 surface.
2. **Exclusion from Capture**: Calls Win32 `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE /* 0x11 */)` on the island window and all popups. This guarantees that the captured frame contains the clean desktop without capturing the island itself, completely eliminating infinite visual feedback loops.
3. **Capture Border Suppression**: On Windows 11 build 22000+, dynamically accesses `GraphicsCaptureSession.IsBorderRequired = false` via reflection to remove the system yellow capture outline.
4. **Zero-Copy Pipeline**: Acquired DirectX 11 textures from the frame pool are copied on the GPU via `Context.CopyResource`, never touching the CPU.

### Path A: In-App Backdrop (Secondary / Container Pipeline)
For internal popups or when running in windowed environments without desktop capture privileges, `VisualBackdrop` captures the parent XAML container into an offscreen bitmap, only refreshing when visual properties change.

---

## 5. Zero-Idle-CPU Guarantee & Performance

Dynamic Island enforces a strict `< 0.1%` idle CPU policy:

1. **Analytical Spring Settling**: When spring animations (width, height, radius, position) reach rest, `IslandWindow.Animation.cs` unhooks from `CompositionTarget.Rendering`.
2. **Dirty-Frame Gating**: If no spring animation is running and the backdrop hasn't produced a new frame, `GlassManager` dispatches 0 draw calls.
3. **Separable Downscaled Blur**: The backdrop Gaussian blur runs at half-resolution ($2\times$ downscale) or quarter-resolution ($4\times$ downscale) using separable 1D passes, reducing texture fill rate by up to $85\%$.
4. **Pre-allocated Constant Buffers**: Structured buffers and constant buffers are mapped with `MapMode.WriteDiscard` or updated with `UpdateSubresource`, avoiding any per-frame garbage collector allocations.

---

## 6. Resource Lifecycle & COM Management

Every native COM interface (`ID3D11Device`, `ID3D11DeviceContext`, `ID3D11Buffer`, `ID3D11ShaderResourceView`, `ID3D11RenderTargetView`, `ID3D11Texture2D`, `ID3D11VertexShader`, `ID3D11PixelShader`, `ID3D11SamplerState`) is deterministically disposed via `Dispose()` implementations conforming to `IDisposable`.

Window destruction hooks in `IslandWindow`, `MiniPlayerWindow`, `NotesPopupWindow`, `ClipboardPopupWindow`, and `BasketWindow` automatically invoke `GlassHost.Detach()`, freeing offscreen textures and detachment registrations without memory leaks across hundreds of open/close cycles.
