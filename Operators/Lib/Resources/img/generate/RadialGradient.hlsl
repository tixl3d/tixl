#include "shared/blend-functions.hlsl"
#include "shared/bias-functions.hlsl"

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
sampler clammpedSampler : register(s1);

float fmod(float x, float y)
{
    return (x - y * floor(x / y));
}

float PingPongRepeat(float x, float pingPong, float repeat)
{
    float baseValue = x;
    float repeatValue = frac(baseValue);
    float pingPongValue = 1.0 - abs(frac(x * 0.5) * 2.0 - 1.0);
    float singlePingPong = abs(x);

    // Select pingpong type: single or repeating
    float pingPongOutput = lerp(singlePingPong, pingPongValue, step(0.5, repeat));

    // Select between base, repeat, or pingpong
    float value = lerp(baseValue, repeatValue, step(0.5, repeat)); // If repeat, use repeatValue
    value = lerp(value, pingPongOutput, step(0.5, pingPong));      // If pingpong, override with pingpong

    // Clamp final result if not repeating
    value = lerp(saturate(value), value, step(0.5, repeat)); // If NOT repeating, clamp to [0..1]

    return value;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;

    float aspectRation = TargetWidth / TargetHeight;
    float2 p = uv;
    p -= 0.5;
    p.x *= aspectRation;

    float c = 0;

    if (PolarOrientation < 0.5)
    {
        c = distance(p, Center * float2(1, -1)) * 2 - Offset * Width;
    }
    else
    {
        p += Center * float2(-1, 1);
        float Radius = 1;
        float l = 2 * length(p) / Radius;

        float2 polar = float2(atan2(p.x, p.y) / 3.141578 / 2 + 0.5, l) + Center - Center.x;
        c = polar.x + Offset * Width;
    }

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
    c = PingPongRepeat(c / Width, PingPong, Repeat);

    float dBiased = ApplyGainAndBias(c, GainAndBias);
    dBiased = clamp(dBiased, 0.001, 0.999);
    float4 gradient = Gradient.Sample(clammpedSampler, float2(dBiased, 0));

    return (IsTextureValid < 0.5) ? gradient : BlendColors(orgColor, gradient, (int)BlendMode);
}