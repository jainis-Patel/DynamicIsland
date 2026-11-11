// ==============================================================================
// Dynamic Island - Separable Gaussian Blur (HLSL Direct3D 11 / ps_5_0)
// High-performance 2-pass horizontal & vertical separable Gaussian blur filter.
// Downscaled to 1/2 or 1/4 resolution for near-zero GPU memory bandwidth cost.
// ==============================================================================

cbuffer BlurConstants : register(b0)
{
    float2 TexelSize;   // 1.0 / Width, 1.0 / Height
    float Radius;       // Blur radius multiplier (e.g. 1.0 .. 4.0)
    float Padding;
};

Texture2D InputTexture : register(t0);
SamplerState LinearClampSampler : register(s0);

struct VS_OUTPUT
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
};

VS_OUTPUT VS_Main(uint vertexID : SV_VertexID)
{
    VS_OUTPUT output;
    float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
    output.Position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
    output.Position.y = -output.Position.y;
    output.UV = uv;
    return output;
}

// 9-tap Gaussian kernel weights (normalized sum = 1.0)
static const float Weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };

float4 PS_BlurHorizontal(VS_OUTPUT input) : SV_Target
{
    float2 uv = input.UV;
    float2 step = float2(TexelSize.x * Radius, 0.0);

    float4 color = InputTexture.Sample(LinearClampSampler, uv) * Weights[0];
    [unroll]
    for (int i = 1; i < 5; ++i)
    {
        float2 offset = step * (float)i;
        color += InputTexture.Sample(LinearClampSampler, uv + offset) * Weights[i];
        color += InputTexture.Sample(LinearClampSampler, uv - offset) * Weights[i];
    }
    return color;
}

float4 PS_BlurVertical(VS_OUTPUT input) : SV_Target
{
    float2 uv = input.UV;
    float2 step = float2(0.0, TexelSize.y * Radius);

    float4 color = InputTexture.Sample(LinearClampSampler, uv) * Weights[0];
    [unroll]
    for (int i = 1; i < 5; ++i)
    {
        float2 offset = step * (float)i;
        color += InputTexture.Sample(LinearClampSampler, uv + offset) * Weights[i];
        color += InputTexture.Sample(LinearClampSampler, uv - offset) * Weights[i];
    }
    return color;
}
