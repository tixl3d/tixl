#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{

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

StructuredBuffer<LegacyPoint> SourcePoints : t0;
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;


[numthreads(64, 1, 1)] void main(uint3 i
                                 : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
    {
        return;
    }

    LegacyPoint p = SourcePoints[i.x];

    float4 pInClipSpace = mul(float4(p.Position,1), CameraToWorld);
    pInClipSpace.xyz /= pInClipSpace.w;
    pInClipSpace.w =1;
    p.Position = pInClipSpace;


    // Transform rotation is kind of tricky. There might be more efficient ways to do this.
    float3x3 orientationDest = float3x3(
        CameraToWorld._m00_m01_m02,
        CameraToWorld._m10_m11_m12,
        CameraToWorld._m20_m21_m22);

    float4 newRotation = normalize(qFromMatrix3Precise(transpose(orientationDest)));
    newRotation = qMul(newRotation, p.Rotation);
    p.Rotation = newRotation;

    ResultPoints[i.x] = p; 
}
