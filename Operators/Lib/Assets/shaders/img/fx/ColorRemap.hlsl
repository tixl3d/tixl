#include "shared/bias-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float DontColorAlpha;
    float Mode;
    float Offset;
    float Exposure;

    float2 GainAndBias;
    float Repeat;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
Texture2D<float4> Gradient : register(t1);
sampler linearSampler : register(s0);
sampler clampedSampler : register(s1);

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float4 orgColor = ImageA.Sample(linearSampler, psInput.texCoord);

    float4 gradient = 0;
    if (Mode < 0.5)
    {
        float gray = (orgColor.r + orgColor.g + orgColor.b) / 3 * Exposure;
        orgColor = ApplyGainAndBias(saturate(gray), GainAndBias) * Repeat;
        gradient = Gradient.Sample(linearSampler, float2(orgColor.r + Offset, 0));
    }
    else
    {
        orgColor.rgb *= Exposure;
        orgColor = ApplyGainAndBias(saturate(orgColor), GainAndBias) * Repeat;

        gradient = float4(
            Gradient.Sample(linearSampler, float2(orgColor.r + Offset, 0)).r,
            Gradient.Sample(linearSampler, float2(orgColor.g + Offset, 0)).g,
            Gradient.Sample(linearSampler, float2(orgColor.b + Offset, 0)).b,
            Gradient.Sample(linearSampler, float2(orgColor.a + Offset, 0)).a);
    }

    gradient.a = DontColorAlpha > 0.5 ? orgColor.a : gradient.a;
    return gradient;
}