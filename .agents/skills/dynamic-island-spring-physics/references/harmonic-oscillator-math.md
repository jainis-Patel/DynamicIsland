# Harmonic Oscillator Mathematical Derivation in Spring.cs

This reference details the analytical solutions of the second-order ordinary differential equation implemented in `Core/Spring.cs`.

---

## 1. Differential Equation

$$\ddot{x}(t) + 2\alpha \dot{x}(t) + \omega_0^2 x(t) = 0$$

Where:
* $x(t) = \text{Value}(t) - \text{Target}$
* $\dot{x}(t) = \text{Velocity}(t)$
* $k = \text{Stiffness} \implies \omega_0 = \sqrt{k}$ (undamped angular frequency)
* $c = \text{Damping} \implies \alpha = \frac{c}{2}$ (exponential decay constant)
* $\zeta = \frac{c}{2\sqrt{k}} = \frac{\alpha}{\omega_0}$ (damping ratio)

---

## 2. Underdamped Solution ($\zeta < 1.0$)

When $\zeta < 1$, the roots of the characteristic polynomial are complex conjugates:
$$r_{1,2} = -\alpha \pm i \omega_d, \quad \text{where } \omega_d = \omega_0 \sqrt{1 - \zeta^2}$$

The general position solution is:
$$x(t) = e^{-\alpha t} \left( a \cos(\omega_d t) + b \sin(\omega_d t) \right)$$

With initial conditions $x(0) = x_0$ and $\dot{x}(0) = v_0$:
$$a = x_0$$
$$b = \frac{v_0 + \alpha x_0}{\omega_d}$$

Differentiating with respect to $t$ gives the instantaneous velocity:
$$\dot{x}(t) = e^{-\alpha t} \left[ (b\omega_d - \alpha a)\cos(\omega_d t) - (a\omega_d + \alpha b)\sin(\omega_d t) \right]$$

In `Spring.cs`, $\sin(\omega_d t)$ and $\cos(\omega_d t)$ are computed in a single hardware instruction via `Math.SinCos(omegaD * dt)`.

---

## 3. Critically Damped Solution ($\zeta = 1.0$)

When $\zeta = 1$, roots are repeated real numbers $r = -\alpha$:
$$x(t) = e^{-\alpha t} (a + b t)$$

Where:
$$a = x_0$$
$$b = v_0 + \alpha x_0$$

The velocity solution:
$$\dot{x}(t) = e^{-\alpha t} \left[ b - \alpha (a + b t) \right]$$

This regime provides the fastest possible return to equilibrium without any overshoot, used when Windows Reduced Motion is active.

---

## 4. Overdamped Solution ($\zeta > 1.0$)

When $\zeta > 1$, roots are distinct negative real numbers:
$$\omega_d = \omega_0 \sqrt{\zeta^2 - 1}$$
$$r_1 = -\alpha + \omega_d, \quad r_2 = -\alpha - \omega_d$$

Position:
$$x(t) = c_1 e^{r_1 t} + c_2 e^{r_2 t}$$

Where:
$$c_2 = \frac{v_0 - r_1 x_0}{r_2 - r_1}, \quad c_1 = x_0 - c_2$$

Velocity:
$$\dot{x}(t) = c_1 r_1 e^{r_1 t} + c_2 r_2 e^{r_2 t}$$

---

## 5. Rest Tolerances

To avoid continuous sub-pixel micro-jitter that keeps the GPU compositor awake:
```csharp
private const double RestEpsilon = 0.05;
public bool IsAtRest => Math.Abs(Target - Value) < RestEpsilon && Math.Abs(Velocity) < RestEpsilon;
```
Once both distance and velocity fall below $0.05$, `Value` is snapped to `Target`, `Velocity` is zeroed, and `IsAtRest` signals the animation loop to sleep.
