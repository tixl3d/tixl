#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float BlendFactor;
    float PairingMethod;
    float CountA;
    float CountB;
}

// struct LegacyPoint
// {
//     float3 position;
//     float w;
//     float4 rotation;
// };

StructuredBuffer<LegacyPoint> PointsB : t0;         // input
RWStructuredBuffer<LegacyPoint> ResultPoints : u0; 

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    //float3 variationOffset = hash31((float)(i.x%1234)/0.123 ) * Variation;

    float3 posA = ResultPoints[i.x].Position;
    float3 posB = PointsB[i.x].Position;
    float wA = ResultPoints[i.x].W;
    float wB = ResultPoints[i.x].W;

    ResultPoints[i.x].Position = lerp(posA, posB, BlendFactor);
    ResultPoints[i.x].W = lerp(wA, wB, BlendFactor); ;
}

