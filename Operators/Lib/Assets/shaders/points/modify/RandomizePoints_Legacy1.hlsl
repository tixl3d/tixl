#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"


cbuffer Params : register(b0)
{
    float3 RandomizePosition;
    float Amount;

    float3 RandomizeRotation;
    float RandomizeW;
    
    float UseLocalSpace;
    float Seed;

    float Bias;
    float Offset;

    float UseWAsSelection;
}

StructuredBuffer<LegacyPoint> SourcePoints : t0;        
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;   


[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    SourcePoints.GetDimensions(pointCount, stride);
    // if(i.x >= pointCount) {
    //     ResultPoints[i.x].w = 0 ;
    //     return;
    // }
    
    LegacyPoint p = SourcePoints[i.x];


    int pointId = i.x;
    float f = pointId / (float)pointCount;
    float phase = Seed + 133.1123 * f + 999;
    int phaseId = (int)phase ; 
    float4 normalizedScatter = lerp(hash41u(pointId * 12341 + phaseId),
                                    hash41u(pointId * 12341 + phaseId + 1),
                                    smoothstep(0, 1,
                                               phase - phaseId));



    float4 hashRot = lerp(hash41u(pointId * 2723 + phaseId),
                                    hash41u(pointId * 2723 + phaseId + 1),
                                    smoothstep(0, 1,
                                               phase - phaseId)) * 2 - 1;


    float4 hash4 =  GetSchlickBias(normalizedScatter, Bias) * 2 -1;


    float4 rot = p.Rotation;

    float amount = Amount * (UseWAsSelection > 0.5 ? p.W : 1);
 
    float3 offset = hash4.xyz * RandomizePosition * amount;

    if(UseLocalSpace < 0.5)
    {
        offset = qRotateVec3(offset, rot);
    }

    p.Position += offset;

    float3 randomRotate = (hashRot.xyz - 0.5) * (RandomizeRotation / 180 * PI) * amount * hash4.xyz;

    rot = normalize(qMul(rot, qFromAngleAxis(randomRotate.x * Offset, float3(1,0,0))));
    rot = normalize(qMul(rot, qFromAngleAxis(randomRotate.y * Offset, float3(0,1,0))));
    rot = normalize(qMul(rot, qFromAngleAxis(randomRotate.z * Offset, float3(0,0,1))));

    p.Rotation = rot;

    p.W += hash4.w *RandomizeW * amount;
    ResultPoints[i.x] = p;
}

