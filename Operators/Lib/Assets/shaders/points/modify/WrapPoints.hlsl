#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"


cbuffer Transforms : register(b0)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

cbuffer Params : register(b1)
{
    float3 Position;
    float __padding;
    float3 Size;
}

StructuredBuffer<LegacyPoint> SourcePoints : t0;        
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;   

float4 GetBias(float4 x, float bias)
{
    return x / ((1 / bias - 2) * (1 - x) + 1);
}

float4 GetSchlickBias(float4 x, float gain)
{
    return x < 0.5 ? GetBias(x * 2.0, gain)/2.0
                    : GetBias(x * 2.0 - 1.0,1.0 - gain)/2.0 + 0.5;
}


// float3 fmod(float3 x, float3 y) {
//     return (x - y * floor(x / y));
// } 

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    if(i.x >= numStructs) {
        //ResultPoints[i.x].w = 0 ;
        return;
    }

    // float rand = (i.x + 0.5) * 1.431 + 111 + floor(Seed+0.5) * 37.1;
    // float4 hash4 = hash42(rand);
    // hash4 =  GetSchlickBias(hash4, clamp(Bias, 0.001, 0.999)) * 2 -1;
    
    // //float4 hashRot = (hash42( i.x * 1.431 + 237 + floor(Seed+ 0.5) * 117.1) * 2 -1);
    // float4 hashRot = hash42( float2(rand, 23.1));

    // float4 rot = SourcePoints[i.x].rotation;

    // float3 offset = hash4.xyz * RandomizePosition * Amount;

    // if (UseLocalSpace > 1.5) 
    // {
    //     offset = qRotateVec3(offset, rot);
    //     offset = mul( float4(offset,0), WorldToObject);
    // }
    // else if(UseLocalSpace < 0.5)
    // {
    //     offset = qRotateVec3(offset, rot);
    // }

    // ResultPoints[i.x].position = SourcePoints[i.x].position + offset;

    // float3 randomRotate = hashRot.xyz * (RandomizeRotation / 180 * PI) * Amount;

    // rot = normalize(qMul(rot, qFromAngleAxis(randomRotate.x * Offset, float3(1,0,0))));
    // rot = normalize(qMul(rot, qFromAngleAxis(randomRotate.y * Offset, float3(0,1,0))));
    // rot = normalize(qMul(rot, qFromAngleAxis(randomRotate.z * Offset, float3(0,0,1))));

    // ResultPoints[i.x].rotation = rot;



    ResultPoints[i.x] =  SourcePoints[i.x];

    float3 a = (SourcePoints[i.x].Position - Position  + Size/2);

    float3 newPosition  =  mod(a, Size)  - Size/2 + Position;

    ResultPoints[i.x].Position = newPosition;
}

