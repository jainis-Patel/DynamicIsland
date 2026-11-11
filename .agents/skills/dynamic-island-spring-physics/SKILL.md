---
name: dynamic-island-spring-physics
description: >-
  Use this skill when tuning, debugging, or extending the analytical spring physics,
  motion curves, pill morphing, or animation frame loops in Dynamic Island. Covers
  closed-form damped harmonic oscillator equations (Core/Spring.cs), 60Hz-240Hz+ frame-rate
  invariance, critically-damped fallback under Windows Reduced Motion, settling tolerances,
  and CompositionTarget.Rendering optimization.
---

# Dynamic Island Spring Physics Runbook

Dynamic Island relies on a closed-form analytical damped harmonic oscillator implemented in [Spring.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Core/Spring.cs) to drive pill width, height, corner radius, and secondary droplet offset.

Reddit users consistently point out that generic desktop overlay clones suffer from jittery, rubber-band, or frame-rate-dependent animations (e.g. looking fine at 60Hz but flying wildly at 144Hz/240Hz, or lagging high-refresh displays). Dynamic Island avoids numerical integration (like Euler or Verlet) and instead evaluates the **exact closed-form solution of the unit-mass damped harmonic oscillator differential equation in $O(1)$ time per frame** using `Math.Exp` and `Math.SinCos`.

---

## Core Physics Principles

### 1. Analytical Evaluation ($O(1)$ Closed Form)
Instead of approximating velocity with `velocity += acceleration * dt`, `Spring.cs` solves:
$$\ddot{x} + c\dot{x} + kx = 0$$

Where:
* $k = \text{Stiffness}$ (governs speed/frequency: $\omega_0 = \sqrt{k}$)
* $c = \text{Damping}$ (governs resistance: $\zeta = \frac{c}{2\sqrt{k}}$)
* $x(t) = \text{Value} - \text{Target}$

See [Harmonic Oscillator Math Reference](./references/harmonic-oscillator-math.md) for the exact equations for underdamped ($\zeta < 1$), critically damped ($\zeta = 1$), and overdamped ($\zeta > 1$).

### 2. Machine-Precision Refresh Rate Invariance
Because the differential equation is solved analytically, evaluating one 60Hz step ($dt = 1/60\text{ s}$) produces the **identical** position value as evaluating four 240Hz steps ($4 \times 1/240\text{ s}$) to within $10^{-9}$ double precision.

### 3. Stalled Frame Clamping
If a heavy background thread or GPU swap stalls the WPF UI thread for several seconds, naive integration would cause the animation to overshoot wildly. `Spring.cs` clamps $dt = \min(dt, 0.1\text{ s})$, preventing numerical instability.

### 4. Zero-Idle Rendering Lifecycle
`CompositionTarget.Rendering` is unhooked when all springs satisfy `IsAtRest`:
```csharp
public bool IsAtRest => Math.Abs(Target - Value) < RestEpsilon && Math.Abs(Velocity) < RestEpsilon;
```
Rest epsilon is set to $0.05\text{ px}$. When resting, `IslandWindow.Animation.cs` unhooks from `CompositionTarget.Rendering`, dropping the animation loop CPU consumption to **0.0%**.

---

## Tuning Motion & Liquid Parameters

In [IslandWindow.Animation.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/IslandWindow.Animation.cs), `ComputeSpringParameters()` maps user settings into stiffness and damping:

```csharp
// Stiffness scales with the square of animation speed (0.5x .. 1.6x)
double stiffness = 210.0 * speed * speed;

// Damping ratio zeta: 1.00 (critically damped) down to 0.56 (liquid gooey bounce)
double zeta = 1.00 - (0.44 * liquid);
double damping = 2.0 * zeta * Math.Sqrt(stiffness);
```

### Motion Presets
* **Snappy & Minimal (0% Liquid)**: $\zeta = 1.00$. Critically damped. Reaches target in minimum time with zero overshoot.
* **Apple Fluid Default (65% Liquid)**: $\zeta \approx 0.71$. Single soft, organic cushion overshoot (~4% overshoot) before settling smoothly.
* **Liquid Bouncy (100% Liquid)**: $\zeta = 0.56$. Plush, expressive morph with subtle liquid reverberation.
* **Windows Reduced Motion**: When `SystemParameters.ClientAreaAnimation` is false or user toggles "Reduce Motion", $\zeta$ is automatically locked to $1.00$ (critically damped) with zero bounce.

---

## Verification & Self-Test

Whenever altering spring parameters or motion loops, run the physics verification suite using [TestPhysics.ps1](./scripts/TestPhysics.ps1):

```powershell
.\.agents\skills\dynamic-island-spring-physics\scripts\TestPhysics.ps1
```

This executes `TestSpringPhysics` in `SelfTestRunner.cs`, asserting:
1. $60\text{ Hz}$, $144\text{ Hz}$, and $240\text{ Hz}$ positions match within $10^{-9}$.
2. Spring reliably reaches `IsAtRest` after settling.
3. 5-second UI freeze clamp protects against NaN/Infinity explosions.
4. Liquid intensity parameter mapping preserves monotonicity.

---

## Detailed References

* [Harmonic Oscillator Math Reference](./references/harmonic-oscillator-math.md)
* [Physics Verification Script](./scripts/TestPhysics.ps1)
