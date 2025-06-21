#include "shared/blend-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float2 Size;
    float4 CornersRadius;
    float Rotation;
    float UniformScale;
    float Width;
    float Offset;
    float PingPong;
    float Repeat;
    float2 GainAndBias;
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

Texture2D<float4> ImageA : register(t0);
Texture2D<float4> Gradient : register(t1);
sampler texSampler : register(s0);
sampler clammpedSampler : register(s1);

float fmod(float x, float y)
{
    return (x - y * floor(x / y));
}

// source: https://iquilezles.org/articles/distfunctions2d/
float sdRoundedBox(in float2 p, in float2 b, in float4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x = (p.y > 0.0) ? r.x : r.y;
    float2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
}

// Function to rotate a point around the origin
inline float2 rotatePoint(float2 p, float angle)
{
    angle = radians(angle); // Convert angle to radians
    float cosAngle = cos(angle);
    float sinAngle = sin(angle);
    return float2(
        p.x * cosAngle + p.y * sinAngle,
        p.x * sinAngle - p.y * cosAngle);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;

    float aspectRation = TargetWidth / TargetHeight;
    float2 p = uv;
    p -= 0.5;
    p.x *= aspectRation;
    p += Center * float2(-1, 1);
    
    // Apply the rotation to the point
    p = rotatePoint(p, Rotation);

    float c = 0;

    c = sdRoundedBox(p, Size * UniformScale, CornersRadius * UniformScale) * 2 - Offset * Width;

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);

    c = PingPongRepeat(c / Width, PingPong, Repeat);

    float dBiased = ApplyGainAndBias(c, GainAndBias);

    dBiased = clamp(dBiased, 0.001, 0.999);
    float4 gradient = Gradient.Sample(clammpedSampler, float2(dBiased, 0));

    return (IsTextureValid < 0.5) ? gradient : BlendColors(orgColor, gradient, (int)BlendMode);
}