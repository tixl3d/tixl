#include "shared/blend-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float2 Size;
    float2 Position;
    float Round;
    float Feather;
    float GradientBias;
    float Rotate;
    float BlendMode;

    float IsTextureValid;
}

// cbuffer TimeConstants : register(b1)
// {
//     float globalTime;
//     float time;
//     float runTime;
//     float beatTime;
// }

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

float sdBox(in float2 p, in float2 b)
{
    float2 d = abs(p) - b;
    return length(
               max(d, float2(0, 0))) +
           min(max(d.x, d.y),
               0.0);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth / TargetHeight;

    float2 p = psInput.texCoord;
    // p.x -= 0.5;
    p -= 0.5;
    p.x *= aspectRatio;

    // Rotate
    float imageRotationRad = (-Rotate - 90) / 180 * 3.141578;

    float sina = sin(-imageRotationRad - 3.141578 / 2);
    float cosa = cos(-imageRotationRad - 3.141578 / 2);

    // p.x *=aspectRatio;

    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x);

    p -= Position * float2(1, -1);

    float d = sdBox(p, Size / 2);

    d = smoothstep(Round / 2 - Feather / 4, Round / 2 + Feather / 4, d);

    float dBiased = GradientBias >= 0
                        ? pow(d, GradientBias + 1)
                        : 1 - pow(clamp(1 - d, 0, 10), -GradientBias + 1);

    float4 c = lerp(Fill, Background, dBiased);

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);

    return (IsTextureValid < 0.5) ? c : BlendColors(orgColor, c, (int)BlendMode);
}