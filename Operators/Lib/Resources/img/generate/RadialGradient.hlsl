#include "shared/blend-functions.hlsl"
#include "shared/bias-functions.hlsl"
#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float Width;
    float Offset;

    float PingPong;
    float Repeat;
    float PolarOrientation;
    float BlendMode;

    float2 GainAndBias;
    float2 Stretch;

    float Noise;

    float IsTextureValid; // Automatically added by _FxShaderSetup
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
Texture2D<float4> Gradient : register(t1);
sampler texSampler : register(s0);
sampler clampedSampler : register(s1);

float fmod(float x, float y)
{
    return (x - y * floor(x / y));
}

float PingPongRepeat(float x, float pingPong, float repeat)
{
    float baseValue = x + 0.5;
    float repeatValue = frac(baseValue);
    float pingPongValue = 1.0 - abs(frac(x * 0.5) * 2.0 - 1.0);
    float singlePingPong = abs(x);

    float pingPongOutput = lerp(singlePingPong, pingPongValue, step(0.5, repeat));
    float value = lerp(baseValue, repeatValue, step(0.5, repeat));
    value = lerp(value, pingPongOutput, step(0.5, pingPong));
    value = lerp(saturate(value), value, step(0.5, repeat));

    return value;
}
float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;

    float aspectRatio = TargetWidth / TargetHeight;
    float2 p = uv - 0.5;
    p.x *= aspectRatio;

    float w = max(abs(Width), 1e-6);
    float dir = sign(Width);

    float c;

    if (PolarOrientation < 0.5)
    {
        float2 d = (p - Center * float2(1, -1)) / Stretch;
        float r = length(d); // radial distance
        c = r * 2.0 / w - lerp(0.5, 1, PingPong);
    }
    else
    {
        p += Center * float2(-1, 1);
        p /= Stretch;
        float angle = atan2(p.x, p.y) / 3.141578; // [-1 .. 1]
        c = angle / w;
    }

    c -= Offset;
    c = PingPongRepeat(c, PingPong, Repeat);

    // Flip gradient direction (center ↔ edge)
    if (dir <= 0)
        c = 1.0 - c;

    // float dBiased = ApplyGainAndBias(c, GainAndBias);
    // dBiased = clamp(dBiased, 0.001, 0.999);

    // float dBiased = ApplyGainAndBias(c, GainAndBias);

    c = clamp(c, 0.001, 0.999);
    float dBiased = ApplyGainAndBias(c, GainAndBias);

    if (Noise > 0)
    {
        dBiased = saturate(dBiased + hash12(p * 1330.1) * Noise);
    }

    float4 gradient = Gradient.Sample(clampedSampler, float2(dBiased, 0));

    if (IsTextureValid < 0.5)
        return gradient;

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
    return BlendColors(orgColor, gradient, (int)BlendMode);
}
