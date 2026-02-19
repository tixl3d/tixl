#include "shared/blend-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float2 Position;
    float Round;
    float Feather;
    float GradientBias;
    float Rotate;
    float Sides;
    float Radius;
    float Curvature;
    float Blades;
    float BlendMode;
    float IsTextureValid;
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
sampler texSampler : register(s0);

static float PI = 3.141592653;
static float TAU = (3.1415926535 * 2);

float mod(float x, float y)
{
    return (x - y * floor(x / y));
}

// based on https://www.shadertoy.com/view/Wll3R2
float sdNgon(in float2 p, in float r, in float n)
{
    // Can precompute these
    float inv_n = 1.0 / n;

    // Perform radial repeat
    float2 rp = float2(atan2(p.y, p.x), length(p)); // into polar coords
    rp.x /= TAU;
    rp.x = mod(rp.x + inv_n * 0.5, inv_n) - 0.5 * inv_n;
    rp.x *= rp.x > 0 ? (1 - saturate(Blades)) : 1;
    rp.y = saturate(lerp(rp.y, r, Curvature));
    rp.x *= TAU;

    p = float2(cos(rp.x), sin(rp.x)) * rp.y; // back to cartesian
    float2 b = float2(r, r);
    b.y = b.x * tan(TAU * inv_n * 0.5);
    float2 d = abs(p) - b;

    float sd = length(max(d, float2(0, 0))) + min(d.x, 0.0);
    return sd;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth / TargetHeight;
    float2 p = psInput.texCoord;
    p -= 0.5;
    p.x *= aspectRatio;

    // Rotate
    float imageRotationRad = (-Rotate - 90) / 180 * 3.141578;

    float sina = sin(-imageRotationRad - 3.141578 / 2);
    float cosa = cos(-imageRotationRad - 3.141578 / 2);

    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x);

    p += Position.yx * float2(1, 1);

    float d = sdNgon(p, Radius, Sides);
    d = smoothstep(Round / 2 - Feather / 4, Round / 2 + Feather / 4, d);

    float dBiased = GradientBias >= 0
                        ? pow(d, GradientBias + 1)
                        : 1 - pow(clamp(1 - d, 0, 10), -GradientBias + 1);

    float4 c = lerp(Fill, Background, dBiased);
    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
    float a = clamp(orgColor.a + c.a - orgColor.a * c.a, 0, 1);

    return (IsTextureValid < 0.5) ? c : BlendColors(orgColor, c, (int)BlendMode);
}