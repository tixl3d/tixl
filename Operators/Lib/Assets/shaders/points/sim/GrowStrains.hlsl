#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/hash-functions.hlsl"

cbuffer Params : register(b0)
{
    float Variation;
    float NoiseAmount;
    float Frequency;
    float Phase;    

    float3 NoiseDistribution;
    float RotationLookupDistance;

    float Length;
    float Width;
    float NoiseDensity;
}

StructuredBuffer<LegacyPoint> PointsA : t0;
StructuredBuffer<LegacyPoint> PointsB : t1;

Texture2D<float4> GrowthMap : register(t2);
sampler texSampler : register(s0);

RWStructuredBuffer<LegacyPoint> ResultPoints : u0;


float3 GetNoise(float3 pos) 
{
    float3 noiseLookup = (pos + Phase ) * Frequency;
    return snoiseVec3(noiseLookup) * NoiseAmount * NoiseDistribution;
}

void GetTranslationAndRotation(float weight, float3 pointPos, float4 rotation, 
                               out float3 offset, out float4 newRotation) 
{    
    offset = GetNoise(pointPos) * weight;

    float3 xDir = qRotateVec3(float3(RotationLookupDistance,0,0), rotation);
    float3 offsetAtPosXDir = GetNoise(pointPos + xDir) * weight;
    float3 rotatedXDir = (pointPos + xDir + offsetAtPosXDir) - (pointPos + offset);

    float3 yDir = qRotateVec3(float3(0, RotationLookupDistance,0), rotation);
    float3 offsetAtPosYDir = GetNoise(pointPos + yDir) * weight;
    float3 rotatedYDir = (pointPos + yDir + offsetAtPosYDir) - (pointPos + offset);

    float3 rotatedXDirNormalized = normalize(rotatedXDir);
    float3 rotatedYDirNormalized = normalize(rotatedYDir);
    
    float3 crossXY = cross(rotatedXDirNormalized, rotatedYDirNormalized);
    float3x3 orientationDest= float3x3(
        rotatedXDirNormalized, 
        cross(crossXY, rotatedXDirNormalized), 
        crossXY );

    newRotation = normalize(qFromMatrix3(transpose(orientationDest)));
}



[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint sourceCount, strideA;
    PointsA.GetDimensions(sourceCount, strideA);
    sourceCount +=1; //For NaN-Separator

    uint targetPosCount, strideB;
    PointsB.GetDimensions(targetPosCount, strideB);

    //uint sourceCount = (uint)(CountA + 0.1);  
    uint sourceIndex = i.x % sourceCount;
    
    if(sourceIndex == sourceCount-1) 
    {
        ResultPoints[i.x].W = sqrt(-1);
    }
    else 
    {
        uint targetIndex = (i.x / sourceCount )  % targetPosCount;
        LegacyPoint A = PointsA[sourceIndex];
        LegacyPoint B = PointsB[targetIndex];

        //ResultPoints[i.x] = A;
        //return;

        float3  pLocal = qRotateVec3(A.Position, B.Rotation);

        float age = B.W;
        float w = A.W;

        float4 attributes = GrowthMap.SampleLevel(texSampler, float2(age,1-w), 0);
        float d = saturate(attributes.r - 0.05);
        if(d < 0.001)
            d = sqrt(-1);

        float4 rotation = qMul(A.Rotation, B.Rotation);

        float noiseWeight = attributes.g;
        float3 offset;
        float4 newRotation;
        float3 variationOffset = hash31(targetIndex) * Variation;

        GetTranslationAndRotation(noiseWeight, pLocal * NoiseDensity + variationOffset, rotation, offset, newRotation);

        ResultPoints[i.x].Position = pLocal * Length + B.Position + offset;

        ResultPoints[i.x].W = d * Width;
        ResultPoints[i.x].Rotation = newRotation;
    }
}
