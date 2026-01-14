#include "shared/bias-functions.hlsl"

cbuffer FloatParams : register(b0)
{
    float4 ColorA;
    float4 ColorB;

    float2 Offset;
    float Scale;
    float Phase;

    float2 GainAndBias;
    float Gain;
    float Lacunarity;

    float Contrast;
}

cbuffer IntParams : register(b2)
{
    int Iterations;
    int Detail;
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

float3 hash33(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.xxy + p.yzz) * p.zyx);
}

// float3 _hash33(float3 p)
// {
//     uint3 q = uint3(p);
//     q = q * 1664525u + 1013904223u;
//     q.x += q.y * q.z;
//     q.y += q.z * q.x;
//     q.z += q.x * q.y;
//     return frac(float3(q) / 4294967296.0);
// }

float grad(float3 cell, float3 pos)
{
    float3 g = normalize(hash33(cell) * 2.0 - 1.0);
    return dot(g, pos);
}

float3 fade(float3 t)
{
    return t * t * t * (t * (t * 6 - 15) + 10);
}
float perlinTileable(float3 p, float3 period)
{
    float3 i = floor(p);
    float3 f = frac(p);

    float3 w = fade(f);

    // Wrap EVERY corner coordinate (not just base i / i+1)
    float3 c000 = fmod(i + float3(0, 0, 0), period);
    float3 c100 = fmod(i + float3(1, 0, 0), period);
    float3 c010 = fmod(i + float3(0, 1, 0), period);
    float3 c110 = fmod(i + float3(1, 1, 0), period);

    float3 c001 = fmod(i + float3(0, 0, 1), period);
    float3 c101 = fmod(i + float3(1, 0, 1), period);
    float3 c011 = fmod(i + float3(0, 1, 1), period);
    float3 c111 = fmod(i + float3(1, 1, 1), period);

    float n000 = grad(c000, f - float3(0, 0, 0));
    float n100 = grad(c100, f - float3(1, 0, 0));
    float n010 = grad(c010, f - float3(0, 1, 0));
    float n110 = grad(c110, f - float3(1, 1, 0));

    float n001 = grad(c001, f - float3(0, 0, 1));
    float n101 = grad(c101, f - float3(1, 0, 1));
    float n011 = grad(c011, f - float3(0, 1, 1));
    float n111 = grad(c111, f - float3(1, 1, 1));

    float nx00 = lerp(n000, n100, w.x);
    float nx10 = lerp(n010, n110, w.x);
    float nx01 = lerp(n001, n101, w.x);
    float nx11 = lerp(n011, n111, w.x);

    float nxy0 = lerp(nx00, nx10, w.y);
    float nxy1 = lerp(nx01, nx11, w.y);

    return lerp(nxy0, nxy1, w.z);
}

inline float fbmPerlinTileable(float3 p, float3 basePeriod)
{
    float sum = 0.0;
    float amp = 0.5;
    float freq = 1.0;

    // Optional normalization so output stays roughly in [-1,1]
    float norm = 0.0;

    [loop] for (float i = 0; i < Iterations; i++)
    {
        // Keep tiling by scaling period with frequency
        float per = basePeriod * freq;

        sum += amp * perlinTileable(p * freq, per);
        norm += amp;
        p += 0.77 * i;

        freq *= Lacunarity; // typically 2.0
        amp *= Gain;        // typically 0.5
    }

    return (norm > 0.0) ? (sum / norm) : 0.0;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord * Detail;
    float3 p = float3(uv * (Scale) + (Offset + 666), Phase);
    float3 basePeriod = float3(Detail.xx, 1024);

    // float2 uv = (psInput.texCoord - 0.5) * BasePeriod;
    // float3 p = float3(uv * (Scale / BasePeriod) + Offset, Phase);
    // float3 basePeriod = float3(BasePeriod.xy, 1024);

    float noise = fbmPerlinTileable(p, basePeriod);
    noise = noise * 0.5 * Contrast + 0.5;
    float f = noise;

    float fBiased = ApplyGainAndBias(f, GainAndBias);

    return lerp(ColorA, ColorB, saturate(fBiased));
}