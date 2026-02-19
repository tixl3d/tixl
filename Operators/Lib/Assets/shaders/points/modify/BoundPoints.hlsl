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
    float UniformScale;
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

    float3 size = Size * UniformScale;
    float3 halfSize = size * 0.5f;
    float3 minBounds = Position - halfSize;
    float3 maxBounds = Position + halfSize;


    ResultPoints[i.x] =  SourcePoints[i.x];

    float3 a = SourcePoints[i.x].Position;

    float3 newPosition  =  clamp(a,minBounds,maxBounds);
    ResultPoints[i.x].Position = newPosition;
}

