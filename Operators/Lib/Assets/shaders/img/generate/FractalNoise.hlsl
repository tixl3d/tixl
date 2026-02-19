#include "shared/bias-functions.hlsl"

// This shader is heavily based on a ShaderToy Project by CandyCat https://www.shadertoy.com/view/4sc3z2

cbuffer ParamConstants : register(b0)
{
    float4 ColorA;
    float4 ColorB;

    float2 Offset; // 8
    float2 Stretch;

    float Scale; // 12
    float Phase;
    float Iterations;
    float __padding;

    float2 GainAndBias; // 16
    float2 WarpOffsetXY;

    float WarpOffsetZ;
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

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

// from https://www.shadertoy.com/view/4djSRW
#define MOD3 float3(.1031, .11369, .13787)

float3 hash33(float3 p3)
{
    p3 = frac(p3 * MOD3);
    p3 += dot(p3, p3.yxz + 19.19);
    return -1.0 + 2.0 * frac(float3((p3.x + p3.y) * p3.z, (p3.x + p3.z) * p3.y, (p3.y + p3.z) * p3.x));
}

float simplex_noise(float3 p)
{
    const float K1 = 0.333333333;
    const float K2 = 0.166666667;

    float3 i = floor(p + (p.x + p.y + p.z) * K1);
    float3 d0 = p - (i - (i.x + i.y + i.z) * K2);

    // thx nikita: https://www.shadertoy.com/view/XsX3zB
    float3 e = step(float3(0, 0, 0), d0 - d0.yzx);
    float3 i1 = e * (1.0 - e.zxy, 1.0 - e.zxy, 1.0 - e.zxy);
    float3 i2 = 1.0 - e.zxy * (1.0 - e);

    float3 d1 = d0 - (i1 - 1.0 * K2);
    float3 d2 = d0 - (i2 - 2.0 * K2);
    float3 d3 = d0 - (1.0 - 3.0 * K2);

    float4 h = max(0.6 - float4(dot(d0, d0), dot(d1, d1), dot(d2, d2), dot(d3, d3)), 0.0);
    float4 n = h * h * h * h * float4(dot(d0, hash33(i)), dot(d1, hash33(i + i1)), dot(d2, hash33(i + i2)), dot(d3, hash33(i + 1.0)));

    return dot(float4(31.316, 31.316, 31.316, 31.316), n);
}

float noise_sum_abs(float3 p)
{
    float f = 0.0;
    p = p * 1.0;
    f += 1.0000 * abs(simplex_noise(p));
    p = 2.0 * p;
    f += 0.5000 * abs(simplex_noise(p));
    p = 2.0 * p;
    f += 0.2500 * abs(simplex_noise(p));
    p = 2.0 * p;
    f += 0.1250 * abs(simplex_noise(p));
    p = 2.0 * p;
    f += 0.0625 * abs(simplex_noise(p));
    p = 2.0 * p;
    return f;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth / TargetHeight;
    float2 uv = psInput.texCoord;
    uv -= 0.5;
    uv *= Stretch * Scale;
    uv += Offset * float2(-1 / aspectRatio, 1) * Scale * Stretch;
    uv.x *= aspectRatio;

    float3 pos = float3(uv, Phase / 10);

    int steps = clamp(Iterations + 0.5, 1.1, 5.1);

    float f = 0.7;
    float scaleFactor = 1;
    for (int i = 0; i < steps; i++)
    {
        float f1 = noise_sum_abs(pos * scaleFactor + float3(12.4, 3, 0) * i);
        pos += f * float3(WarpOffsetXY, WarpOffsetZ);
        f *= sin(f1) / 2 + 0.5;
        f += 0.2;
    }
    f = 2 * f - 1;

    // float fBiased = f;
    float fBiased = ApplyGainAndBias(f, GainAndBias);

    return lerp(ColorA, ColorB, saturate(fBiased));
}