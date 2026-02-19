#include "shared/blend-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float Width;
    float Rotation;

    float PingPong;
    float Repeat;
    float2 GainAndBias;

    float Offset;
    float SizeMode;
    float BlendMode;
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

inline float fmod(float x, float y)
{
    return (x - y * floor(x / y));
}

float PingPongRepeat(float x, float pingPong, float repeat)
{
    float baseValue = x + 0.5;
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

    if (SizeMode < 0.5)
    {
        p.x *= aspectRation;
    }
    else
    {
        p.y /= aspectRation;
    }

    float radians = Rotation / 180 * 3.141578;
    float2 angle = float2(sin(radians), cos(radians));

    float c = dot(p - Center, angle);
    c += Offset;
    c = PingPongRepeat(c / Width, PingPong > 0.5, Repeat > 0.5);

    float dBiased = ApplyGainAndBias(saturate(c), GainAndBias);
    dBiased = clamp(dBiased, 0.000001, 0.99999);

    float4 gradient = Gradient.Sample(clampedSampler, float2(dBiased, 0));

    if (IsTextureValid < 0.5)
        return gradient;

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
    return (IsTextureValid < 0.5) ? gradient : BlendColors(orgColor, gradient, (int)BlendMode);
}