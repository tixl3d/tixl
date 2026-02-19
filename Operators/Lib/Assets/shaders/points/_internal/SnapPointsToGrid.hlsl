#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 GridStretch;
    float Amount;

    float3 GridOffset;
    float GridScale;

    float Scatter;
    float Mode;
    float2 GainAndBias;
}

cbuffer IntParams : register(b1)
{
    int StrengthFactor;
}

StructuredBuffer<Point> Points1 : t0;
RWStructuredBuffer<Point> ResultPoints : u0;

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    Point p = Points1[i.x];

    float3 gridSize = GridScale * GridStretch;
    float3 orgPosition = p.Position;

    float3 pos = orgPosition;
    float3 normalizedPosition = pos / gridSize;
    float3 normlizedOffsetPosition = normalizedPosition + 0.5 - GridOffset;
    float3 signedFraction = (mod(normlizedOffsetPosition, 1) - 0.5) * 2;
    float3 centerPoint = pos - signedFraction * gridSize / 2;

    float3 scatter = (hash41u(i.x) - 0.5) * Scatter;

    float3 snapAmount = 0;
    if (Mode < 0.5)
    {
        snapAmount = saturate((length(signedFraction * gridSize) / length(gridSize) + scatter.x));
    }
    else if (Mode < 1.5)
    {
        snapAmount = 1 - saturate((length(signedFraction * gridSize) / length(gridSize) + scatter.x));
    }
    else if (Mode < 2.5)
    {
        snapAmount = abs(signedFraction + scatter);
    }
    else if (Mode < 3.5)
    {
        snapAmount = 1 - abs(signedFraction + scatter);
    }

    float3 biasedSnap = ApplyGainAndBias(snapAmount.xyzz, GainAndBias).xyz;

    float strength = Amount * (StrengthFactor == 0
                                     ? 1
                                 : (StrengthFactor == 1) ? p.FX1
                                                         : p.FX2);

    float3 ff = (1 - saturate(biasedSnap - Amount * 2 + 1)) * strength;
    p.Position = lerp(orgPosition, centerPoint, ff);
    ResultPoints[i.x] = p;
}
