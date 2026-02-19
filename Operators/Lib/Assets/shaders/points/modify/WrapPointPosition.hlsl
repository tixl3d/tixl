#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 Center;
    float UseCamera;
    float3 Size;
    float WriteLineBreaks;
}

cbuffer Transforms : register(b1)
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

// struct LegacyPoint {
//     float3 Position;
//     float W;
// };

RWStructuredBuffer<LegacyPoint> ResultPoints : u0; 




[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;

    float3 center = Center;
    if(UseCamera > 0.5) {
        center = float3(CameraToWorld._m30, CameraToWorld._m31, CameraToWorld._m32);
    }

    ResultPoints.GetDimensions(numStructs, stride);
    if(i.x >= numStructs) {
        ResultPoints[i.x].W = sqrt(-1) ;
        return;
    }

    float3 p = ResultPoints[i.x].Position - center;

    if(isnan( p.x + p.y + p.x)    ) {
         ResultPoints[i.x].W = 0.010;
         ResultPoints[i.x].Position = center - Size * 0.2; // some not in center
         return;
    }    

    float3 Padding = Size.x * 0.1;

    float3 halfSize = Size/2;
    float3 padded = halfSize + Padding;

    float3 offsetFactor = 0;

    if(abs(p.x) > padded.x ) { offsetFactor.x = p.x < 0 ? 1 : -1; }
    if(abs(p.y) > padded.y ) { offsetFactor.y = p.y < 0 ? 1 : -1; }
    if(abs(p.z) > padded.z ) { offsetFactor.z = p.z < 0 ? 1 : -1; }
    
    float3 wrappedP =  p + Size * offsetFactor;
    ResultPoints[i.x].Position = wrappedP + center;

    // Add line break for all wraps
    if(WriteLineBreaks > 0.5 && abs(offsetFactor.x) +abs(offsetFactor.y) + abs(offsetFactor.z) !=0 ) {
        ResultPoints[i.x].W = sqrt(-1);
    }
    else 
    {
        float3 distToEdge = halfSize - abs(wrappedP);
        float3 minDist = saturate(distToEdge * 10);
        float minD = minDist.x * minDist.y * minDist.z;
        ResultPoints[i.x].W = minD;
    }
}

