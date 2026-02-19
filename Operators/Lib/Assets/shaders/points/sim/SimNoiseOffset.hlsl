#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float Amount;
    float Frequency;
    float Phase;
    float Variation;
    float3 AmountDistribution;
    float RotationLookupDistance;
    float UseCurlNoise;
}

// struct LegacyPoint {
//     float3 Position;
//     float W;
// };

RWStructuredBuffer<LegacyPoint> ResultPoints : u0; 

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount, stride);
    if(i.x >= pointCount) {
        return;
    }

    float3 variationOffset = hash31((float)(i.x%1234)/0.123 ) * Variation;

    float3 pos = ResultPoints[i.x].Position*0.9; // avoid simplex noice glitch at -1,0,0 
    float3 noiseLookup = (pos + variationOffset + Phase* float3(1,-1,0)  ) * Frequency;

    float3 noise = UseCurlNoise < 0.5 
        ? snoiseVec3(noiseLookup) * Amount/100 * AmountDistribution
        : curlNoise(noiseLookup) * Amount/100 * AmountDistribution;

    float3 n = float3(1, 0.0, 0) * RotationLookupDistance;

    float3 posNormal = ResultPoints[i.x].Position*0.9; // avoid simplex noice glitch at -1,0,0 
    float3 noiseLookupNormal = (posNormal + variationOffset + Phase * float3(0,-1,0)  ) * Frequency + n/Frequency;
    float3 noiseNormal = UseCurlNoise < 0.5
        ? snoiseVec3(noiseLookup) * Amount/100 * AmountDistribution
        : curlNoise(noiseLookup) * Amount/100 * AmountDistribution;
    float4 rotationFromDisplace = normalize(qFromVectors(normalize(n), normalize(n+ noiseNormal) ) );

    ResultPoints[i.x].Position += noise ;
    ResultPoints[i.x].Rotation = qMul(rotationFromDisplace , ResultPoints[i.x].Rotation);
}

