#include "shared/bias-functions.hlsl"

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 MapBlackTo;
    float4 MapWhiteTo;
    float4 ChannelWeights;
    float Amount;
    float2 GainAndBias;
    float Exposure;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 c = inputTexture.SampleLevel(texSampler, uv, 0.0);
    c.rgb *= Exposure;

    float t = length(c * normalize(ChannelWeights)) + 0.001;

    t = ApplyGainAndBias(saturate(t), GainAndBias);
    float4 mapped = lerp(MapBlackTo, MapWhiteTo, t);
    float4 cout = lerp(c, mapped, Amount);
    cout.a = clamp(cout.a, 0, 1);
    return cout;
}
