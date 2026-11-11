namespace DynamicIsland.LiquidGlass;

/// <summary>
/// Primitive geometrical shapes supported by the Liquid Glass SDF evaluator.
/// </summary>
public enum GlassShapeType : uint
{
    /// <summary>Rounded rectangle with independent or symmetric corner radii.</summary>
    RoundedRect = 0,

    /// <summary>Continuous-curvature squircle (iOS / macOS superellipse profile).</summary>
    Squircle = 1,

    /// <summary>Pill / Capsule (hemispherical caps on opposing ends).</summary>
    Capsule = 2,

    /// <summary>Pure Euclidean circle.</summary>
    Circle = 3
}

/// <summary>
/// Geometric descriptor for a single glass body rendered in the scene.
/// Multiple bodies are smoothly unified via polynomial smooth-minimum ($s\min$).
/// </summary>
public readonly record struct GlassShape(
    float CenterX,
    float CenterY,
    float HalfWidth,
    float HalfHeight,
    float CornerRadius,
    GlassShapeType ShapeType = GlassShapeType.RoundedRect)
{
    public static GlassShape FromBounds(float x, float y, float width, float height, float cornerRadius, GlassShapeType type = GlassShapeType.RoundedRect)
    {
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        return new GlassShape(x + hw, y + hh, hw, hh, cornerRadius, type);
    }
}
