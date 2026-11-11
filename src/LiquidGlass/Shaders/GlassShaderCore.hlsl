// ==============================================================================
// Dynamic Island - Real Liquid Glass Shader Core (HLSL Direct3D 11 / ps_5_0)
// Physically-based Snell's law refraction, analytic SDF normals, specular rim,
// separable backdrop blur sampling, and multi-body smooth-union (smin).
// ==============================================================================

#define MAX_SHAPES 16

struct ShapeData
{
    float2 Center;        // Center in physical pixels
    float2 HalfSize;      // Half-dimensions (width/2, height/2) in physical pixels
    float CornerRadius;   // Corner radius in physical pixels
    uint ShapeType;       // 0 = RoundedRect, 1 = Squircle, 2 = Capsule, 3 = Circle
    float2 Padding;
};

cbuffer GlassConstants : register(b0)
{
    float2 ViewportSize;        // Viewport width, height (physical pixels)
    float DpiScale;             // DPI scaling factor
    uint ShapeCount;            // Number of active shapes (1..MAX_SHAPES)

    float Thickness;            // Refraction displacement scale
    float BevelWidth;           // Edge bevel lens width in pixels
    float IndexOfRefraction;    // Glass IOR (e.g. 1.5)
    float BlurRadius;           // Backdrop blur radius

    float Saturation;           // Backdrop saturation multiplier
    float Brightness;           // Backdrop brightness multiplier
    float SpecularStrength;     // Directional specular rim intensity
    float RimPower;             // Specular rim falloff exponent

    float4 TintColor;           // RGB tint + A tint opacity

    float2 LightDirection;      // 2D normalized direction to light source
    float Dispersion;           // Chromatic dispersion intensity (0 = off)
    float EdgeFeather;          // Anti-aliased edge feather in pixels

    float CornerSmoothing;      // Continuous curvature smoothing (0 = standard, 1 = squircle)
    float MergeSmoothness;      // Smooth-union polynomial factor (k)
    float2 ScreenOffset;        // Element's top-left corner in physical monitor pixels
    float2 BackdropSize;        // Full monitor capture texture size in pixels
    float2 Reserved;

    ShapeData Shapes[MAX_SHAPES];
};

Texture2D BackdropTexture : register(t0);
Texture2D BlurredBackdropTexture : register(t1);
SamplerState LinearClampSampler : register(s0);

struct VS_OUTPUT
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
};

// ------------------------------------------------------------------------------
// Vertex Shader: Fullscreen Triangle (no vertex buffer needed)
// ------------------------------------------------------------------------------
VS_OUTPUT VS_Main(uint vertexID : SV_VertexID)
{
    VS_OUTPUT output;
    // Generates a large triangle that covers the full clip space [-1, 1]
    float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
    output.Position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
    // Direct3D screen space Y is flipped compared to UV
    output.Position.y = -output.Position.y;
    output.UV = uv;
    return output;
}

// ------------------------------------------------------------------------------
// Signed Distance Field (SDF) Primitives
// ------------------------------------------------------------------------------

// Rounded Rectangle SDF
float sdRoundRect(float2 p, float2 halfSize, float r)
{
    r = min(r, min(halfSize.x, halfSize.y));
    float2 q = abs(p) - halfSize + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

// Squircle / Continuous Corner SDF (iOS style superellipse approximation)
float sdSquircle(float2 p, float2 halfSize, float r, float smoothing)
{
    r = min(r, min(halfSize.x, halfSize.y));
    float2 q = abs(p) - halfSize + r;
    if (q.x <= 0.0 || q.y <= 0.0)
    {
        return min(max(q.x, q.y), 0.0) - r;
    }
    // Blend p-norm from 2.0 (circle) up to ~4.0 (squircle) based on smoothing
    float p_val = lerp(2.0, 4.0, saturate(smoothing));
    float d = pow(pow(abs(q.x), p_val) + pow(abs(q.y), p_val), 1.0 / p_val) - r;
    return d;
}

// Capsule SDF
float sdCapsule(float2 p, float2 halfSize)
{
    float r = halfSize.y;
    float halfWidth = max(0.0, halfSize.x - r);
    float2 q = float2(max(abs(p.x) - halfWidth, 0.0), p.y);
    return length(q) - r;
}

// Circle SDF
float sdCircle(float2 p, float r)
{
    return length(p) - r;
}

// Single shape evaluator
float EvaluateShapeSDF(float2 pixelPos, ShapeData s, float smoothing)
{
    float2 p = pixelPos - s.Center;
    switch (s.ShapeType)
    {
        case 1: // Squircle
            return sdSquircle(p, s.HalfSize, s.CornerRadius, smoothing);
        case 2: // Capsule
            return sdCapsule(p, s.HalfSize);
        case 3: // Circle
            return sdCircle(p, min(s.HalfSize.x, s.HalfSize.y));
        default: // Rounded Rectangle
            return sdRoundRect(p, s.HalfSize, s.CornerRadius);
    }
}

// Smooth Minimum (Polynomial smin) for liquid droplet union
float smin(float a, float b, float k)
{
    if (k <= 0.0001) return min(a, b);
    float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// Global scene distance field with smooth-union
float SceneSDF(float2 pixelPos)
{
    if (ShapeCount == 0) return 1000.0;

    float d = EvaluateShapeSDF(pixelPos, Shapes[0], CornerSmoothing);
    for (uint i = 1; i < ShapeCount; ++i)
    {
        float dOther = EvaluateShapeSDF(pixelPos, Shapes[i], CornerSmoothing);
        d = smin(d, dOther, MergeSmoothness);
    }
    return d;
}

// ------------------------------------------------------------------------------
// Color Space & Optics Helpers
// ------------------------------------------------------------------------------

float3 LinearToSRGB(float3 color)
{
    return saturate(pow(max(color, 0.0), 1.0 / 2.2));
}

float3 SRGBToLinear(float3 color)
{
    return saturate(pow(max(color, 0.0), 2.2));
}

float3 AdjustSaturation(float3 color, float sat)
{
    float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
    return lerp(luma.xxx, color, sat);
}

// ------------------------------------------------------------------------------
// Pixel Shader: Real Liquid Glass
// ------------------------------------------------------------------------------
float4 PS_Main(VS_OUTPUT input) : SV_Target
{
    float2 pixelPos = input.Position.xy;

    // 1. Evaluate Scene Signed Distance Field
    float d = SceneSDF(pixelPos);

    float feather = max(EdgeFeather, 0.5);
    // If pixel is entirely outside the glass silhouette + anti-aliased edge, discard
    if (d > feather)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    // 2. High-precision central-difference gradient for surface normal derivation
    float eps = 0.5;
    float dx = SceneSDF(pixelPos + float2(eps, 0.0)) - SceneSDF(pixelPos - float2(eps, 0.0));
    float dy = SceneSDF(pixelPos + float2(0.0, eps)) - SceneSDF(pixelPos - float2(0.0, eps));
    float2 grad = float2(dx, dy);
    float gradLen = length(grad);
    float2 g = (gradLen > 1e-5) ? (grad / gradLen) : float2(0.0, 0.0);

    // 3. Convex Bevel Profile: Rim bulges like a lens, interior remains flat
    float s = max(-d, 0.0); // Penetration depth inside glass
    float bevel = max(BevelWidth, 1.0);
    float a = saturate(1.0 - s / bevel); // 1.0 at outer rim, tapering to 0.0 in flat interior

    // Convex curved lens normal:
    float normalZ = sqrt(max(1.0 - a * a, 1e-4));
    float3 n = normalize(float3(g * a, normalZ));

    // 4. Snell's Law Refraction
    float3 incident = float3(0.0, 0.0, -1.0); // Viewing direction toward screen
    float ior = max(IndexOfRefraction, 1.001);

    // Primary refraction ray
    float3 R = refract(incident, n, 1.0 / ior);
    float rz = (abs(R.z) > 1e-4) ? -R.z : 1.0;
    float2 offsetPixels = (R.xy / rz) * (Thickness * DpiScale);

    // 5. Backdrop Sampling with Refraction & Optional Chromatic Dispersion
    float3 backdropColor;

    // Compute viewport-relative screen coordinates for correct backdrop sampling:
    // ScreenOffset maps this element's local pixel coordinates into monitor-space.
    // BackdropSize is the full monitor capture dimensions. If BackdropSize is zero
    // (legacy fallback), fall back to ViewportSize for backward compatibility.
    float2 effectiveBackdropSize = (BackdropSize.x > 1.0) ? BackdropSize : ViewportSize;
    float2 screenPos = pixelPos + ScreenOffset;
    float2 baseUV = (screenPos + offsetPixels) / effectiveBackdropSize;
    baseUV = clamp(baseUV, float2(0.001, 0.001), float2(0.999, 0.999));

    // Blend between sharp backdrop and blurred backdrop based on BlurRadius
    float blurWeight = saturate(BlurRadius / 25.0);

    if (Dispersion > 0.001)
    {
        // Sample Red with lower IOR (longer wavelength), Blue with higher IOR
        float disp = Dispersion * 0.08;
        float3 R_red = refract(incident, n, 1.0 / (ior * (1.0 - disp)));
        float3 R_blue = refract(incident, n, 1.0 / (ior * (1.0 + disp)));

        float2 offset_r = (R_red.xy / max(-R_red.z, 1e-4)) * (Thickness * DpiScale);
        float2 offset_b = (R_blue.xy / max(-R_blue.z, 1e-4)) * (Thickness * DpiScale);

        float2 uv_r = clamp((screenPos + offset_r) / effectiveBackdropSize, 0.001, 0.999);
        float2 uv_b = clamp((screenPos + offset_b) / effectiveBackdropSize, 0.001, 0.999);

        float3 sharp_r = BackdropTexture.Sample(LinearClampSampler, uv_r).rgb;
        float3 sharp_g = BackdropTexture.Sample(LinearClampSampler, baseUV).rgb;
        float3 sharp_b = BackdropTexture.Sample(LinearClampSampler, uv_b).rgb;

        float3 blur_r = BlurredBackdropTexture.Sample(LinearClampSampler, uv_r).rgb;
        float3 blur_g = BlurredBackdropTexture.Sample(LinearClampSampler, baseUV).rgb;
        float3 blur_b = BlurredBackdropTexture.Sample(LinearClampSampler, uv_b).rgb;

        float3 sharpCol = float3(sharp_r.r, sharp_g.g, sharp_b.b);
        float3 blurCol = float3(blur_r.r, blur_g.g, blur_b.b);
        backdropColor = lerp(sharpCol, blurCol, blurWeight);
    }
    else
    {
        float3 sharpCol = BackdropTexture.Sample(LinearClampSampler, baseUV).rgb;
        float3 blurCol = BlurredBackdropTexture.Sample(LinearClampSampler, baseUV).rgb;
        backdropColor = lerp(sharpCol, blurCol, blurWeight);
    }

    // 6. Color Vibrancy & Optical Enhancements (Linear color processing)
    float3 linearBackdrop = SRGBToLinear(backdropColor);
    linearBackdrop = AdjustSaturation(linearBackdrop, Saturation);
    linearBackdrop *= Brightness;

    // Apply Tint Color (tint is in sRGB, convert to linear)
    float3 linearTint = SRGBToLinear(TintColor.rgb);
    float3 col = lerp(linearBackdrop, linearTint, TintColor.a);

    // 7. Directional Specular Rim Lighting
    float2 L = normalize(LightDirection);
    float dotL = dot(g, L);
    float primaryRim = saturate(dotL);
    float counterRim = 0.35 * saturate(-dotL);
    float ambientRim = 0.08;

    // Lens edge power profile
    float rimShape = pow(a, max(RimPower, 0.5));
    float rimTotal = rimShape * (primaryRim + counterRim + ambientRim) * SpecularStrength;

    // Subtle inner bevel caustic highlight
    float innerGlow = pow(saturate(1.0 - abs(s - bevel * 0.4) / (bevel * 0.4)), 3.0) * 0.12 * SpecularStrength;

    // Add white specular rim & caustic highlight to linear color
    col += (rimTotal + innerGlow).xxx;

    // 8. Anti-aliased outer silhouette alpha
    float alpha = smoothstep(feather, 0.0, d);

    // Convert back to sRGB for display presentation
    float3 finalSRGB = LinearToSRGB(col);

    // Return premultiplied-alpha color for DirectComposition / DXGI
    return float4(finalSRGB * alpha, alpha);
}
