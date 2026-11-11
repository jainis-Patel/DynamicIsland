using System.Numerics;
using System.Windows.Media;

namespace DynamicIsland.LiquidGlass;

/// <summary>
/// Immutable configuration parameters governing physical refraction, surface normal derivation,
/// optics, and lighting for the GPU Liquid Glass rendering engine.
/// </summary>
public sealed record GlassSettings
{
    public float Thickness { get; init; } = 18.0f;
    public float BevelWidth { get; init; } = 22.0f;
    public float IndexOfRefraction { get; init; } = 1.50f;
    public float BlurRadius { get; init; } = 20.0f;

    public float Saturation { get; init; } = 1.20f;
    public float Brightness { get; init; } = 1.05f;

    public Color TintColor { get; init; } = Color.FromArgb(0x60, 0x0A, 0x0A, 0x10);

    public float SpecularStrength { get; init; } = 0.85f;
    public Vector2 LightDirection { get; init; } = Vector2.Normalize(new Vector2(-0.577f, -0.816f));
    public float RimPower { get; init; } = 2.5f;

    public float Dispersion { get; init; } = 0.0f;
    public float EdgeFeather { get; init; } = 1.0f;

    public float CornerRadius { get; init; } = 18.0f;
    public float CornerSmoothing { get; init; } = 0.80f;
    public GlassShapeType Shape { get; init; } = GlassShapeType.RoundedRect;
    public float MergeSmoothness { get; init; } = 16.0f;

    public Builder ToBuilder() => new Builder(this);

    public static Builder Create() => new Builder();

    public sealed class Builder
    {
        private GlassSettings _settings;

        public Builder() => _settings = new GlassSettings();

        public Builder(GlassSettings source) => _settings = source;

        public Builder WithThickness(float thickness)
        {
            _settings = _settings with { Thickness = Math.Clamp(thickness, 0.0f, 100.0f) };
            return this;
        }

        public Builder WithBevelWidth(float bevelWidth)
        {
            _settings = _settings with { BevelWidth = Math.Clamp(bevelWidth, 1.0f, 100.0f) };
            return this;
        }

        public Builder WithIndexOfRefraction(float ior)
        {
            _settings = _settings with { IndexOfRefraction = Math.Clamp(ior, 1.001f, 3.0f) };
            return this;
        }

        public Builder WithBlurRadius(float blurRadius)
        {
            _settings = _settings with { BlurRadius = Math.Clamp(blurRadius, 0.0f, 100.0f) };
            return this;
        }

        public Builder WithSaturation(float saturation)
        {
            _settings = _settings with { Saturation = Math.Clamp(saturation, 0.0f, 3.0f) };
            return this;
        }

        public Builder WithBrightness(float brightness)
        {
            _settings = _settings with { Brightness = Math.Clamp(brightness, 0.0f, 3.0f) };
            return this;
        }

        public Builder WithTint(Color color)
        {
            _settings = _settings with { TintColor = color };
            return this;
        }

        public Builder WithTint(byte r, byte g, byte b, float alpha = 0.4f)
        {
            byte a = (byte)Math.Clamp((int)(alpha * 255f), 0, 255);
            _settings = _settings with { TintColor = Color.FromArgb(a, r, g, b) };
            return this;
        }

        public Builder WithSpecular(float strength, float rimPower = 2.5f)
        {
            _settings = _settings with
            {
                SpecularStrength = Math.Clamp(strength, 0.0f, 5.0f),
                RimPower = Math.Clamp(rimPower, 0.2f, 10.0f)
            };
            return this;
        }

        public Builder WithLightDirection(float x, float y)
        {
            var vec = new Vector2(x, y);
            if (vec.LengthSquared() > 1e-4f)
            {
                vec = Vector2.Normalize(vec);
            }
            _settings = _settings with { LightDirection = vec };
            return this;
        }

        public Builder WithDispersion(float dispersion)
        {
            _settings = _settings with { Dispersion = Math.Clamp(dispersion, 0.0f, 1.0f) };
            return this;
        }

        public Builder WithEdgeFeather(float feather)
        {
            _settings = _settings with { EdgeFeather = Math.Clamp(feather, 0.5f, 4.0f) };
            return this;
        }

        public Builder WithCornerRadius(float cornerRadius)
        {
            _settings = _settings with { CornerRadius = Math.Max(0.0f, cornerRadius) };
            return this;
        }

        public Builder WithCornerSmoothing(float smoothing)
        {
            _settings = _settings with { CornerSmoothing = Math.Clamp(smoothing, 0.0f, 1.0f) };
            return this;
        }

        public Builder WithShape(GlassShapeType shape)
        {
            _settings = _settings with { Shape = shape };
            return this;
        }

        public Builder WithMergeSmoothness(float smoothness)
        {
            _settings = _settings with { MergeSmoothness = Math.Max(0.0f, smoothness) };
            return this;
        }

        public GlassSettings Build() => _settings;
    }
}
