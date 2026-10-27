namespace DynamicIsland.Core;

/// <summary>
/// Closed-form analytical damped harmonic spring (unit mass).
/// Evaluates the exact differential equation solution in O(1) time per frame using Math.Exp and Math.SinCos,
/// guaranteeing machine-precision frame-rate invariance across 60 Hz, 144 Hz, 240 Hz, and VRR displays.
/// Default stiffness/damping gives a damping ratio of ~0.73: one soft overshoot, like iOS.
/// </summary>
public sealed class Spring
{
    private const double RestEpsilon = 0.05;

    public double Value { get; set; }
    public double Velocity { get; set; }
    public double Target { get; set; }
    public double Stiffness { get; set; } = 320;
    public double Damping { get; set; } = 26;

    public Spring(double initial)
    {
        Value = Target = initial;
    }

    public bool IsAtRest => Math.Abs(Target - Value) < RestEpsilon && Math.Abs(Velocity) < RestEpsilon;

    public void Step(double dt)
    {
        if (dt <= 0) return;
        dt = Math.Min(dt, 0.1); // Clamp after a long UI stall

        double x0 = Value - Target;
        double v0 = Velocity;

        if (Math.Abs(x0) < 1e-9 && Math.Abs(v0) < 1e-9)
        {
            Value = Target;
            Velocity = 0;
            return;
        }

        double omega0 = Math.Sqrt(Stiffness);
        double zeta = Damping / (2.0 * omega0);
        double alpha = 0.5 * Damping;

        double x, v;

        if (zeta < 1.0 - 1e-5)
        {
            // Underdamped harmonic oscillator (standard iOS-style spring)
            double omegaD = omega0 * Math.Sqrt(1.0 - zeta * zeta);
            double a = x0;
            double b = (v0 + alpha * x0) / omegaD;

            double decay = Math.Exp(-alpha * dt);
            var (sin, cos) = Math.SinCos(omegaD * dt);

            x = decay * (a * cos + b * sin);
            v = decay * ((b * omegaD - alpha * a) * cos - (a * omegaD + alpha * b) * sin);
        }
        else if (zeta > 1.0 + 1e-5)
        {
            // Overdamped
            double omegaD = omega0 * Math.Sqrt(zeta * zeta - 1.0);
            double r1 = -alpha + omegaD;
            double r2 = -alpha - omegaD;
            double c2 = (v0 - r1 * x0) / (r2 - r1);
            double c1 = x0 - c2;

            double e1 = Math.Exp(r1 * dt);
            double e2 = Math.Exp(r2 * dt);

            x = c1 * e1 + c2 * e2;
            v = c1 * r1 * e1 + c2 * r2 * e2;
        }
        else
        {
            // Critically damped
            double a = x0;
            double b = v0 + alpha * x0;
            double decay = Math.Exp(-alpha * dt);

            x = decay * (a + b * dt);
            v = decay * (b - alpha * (a + b * dt));
        }

        Value = Target + x;
        Velocity = v;

        if (IsAtRest)
        {
            Value = Target;
            Velocity = 0;
        }
    }
}
