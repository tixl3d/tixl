// #include "shared/bias-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float NumSamples;
    float Length;

    float4 RayColor;

    float Decay;
    float ApplyFxToBackground;
    float Amount;
    float RefineFactor;
    float RefineSamples;

    float AspectRatio;
}

// cbuffer Resolution : register(b1)
// {
//     float TargetWidth;
//     float TargetHeight;
// }

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
Texture2D<float4> FxImage : register(t1);
Texture2D<float4> RayImage : register(t2);

sampler texSampler : register(s0);

float4 psMain(vsOutput input) : SV_TARGET
{
    float2 centerproof = Center * float2(1, -1) + float2(0.5, 0.5);

    float2 uv = input.texCoord;

    float4 orgColor = Image.Sample(texSampler, uv) * lerp(1, FxImage.Sample(texSampler, uv), ApplyFxToBackground);

    float2 delta = (uv - centerproof) * Length / NumSamples;
    float4 colorSum = 0;

    for (int i = 1; i < NumSamples; i++)
    {
        uv -= delta;
        colorSum += Image.Sample(texSampler, uv) * FxImage.Sample(texSampler, uv) * pow(1 - (float)i / NumSamples, Decay);
    }

    return clamp(orgColor + colorSum / NumSamples * RayColor * Amount, 0, float4(1000, 1000, 1000, 1));
}

// An ill-fated attempt of a refinement pass.
// Sadly it has too many artifacts to be usable.
float4 Pass2Refine(vsOutput input) : SV_TARGET
{

    float2 centerproof = Center * float2(1, -1) + float2(0.5, 0.5);

    float2 uv = input.texCoord;

    float4 orgColor = Image.Sample(texSampler, uv) * lerp(1, FxImage.Sample(texSampler, uv), ApplyFxToBackground);

    int refineSampleCount = 20;

    float extra = length((uv - centerproof) * float2(1, 1));
    float2 delta = (uv - centerproof) * Length / refineSampleCount * RefineFactor * extra;
    float4 colorSum = 0;

    uv += delta * RefineSamples / 2;
    for (int i = 1; i < RefineSamples; i++)
    {
        uv -= delta;
        colorSum += RayImage.Sample(texSampler, uv); // * FxImage.Sample(texSampler, uv) * pow(1 - (float)i / NumSamples, Decay);
    }

    return clamp(orgColor + colorSum / NumSamples * RayColor * Amount, 0, float4(1000, 1000, 1000, 1));
}