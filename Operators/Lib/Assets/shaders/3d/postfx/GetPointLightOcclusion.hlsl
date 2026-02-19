#include "shared/point-light.hlsl"

Texture2D<float> DepthTexture : register(t0);
RWTexture2D<float> OutputTexture : register(u0);

cbuffer ParamConstants : register(b0)
{
    float Near;
    float Far;
}

cbuffer PointLights : register(b1)
{
    PointLight Lights[8];
    int ActiveLightCount;
}

cbuffer Transforms : register(b2)
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

sampler texSampler : register(s0);

[numthreads(8,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{

    uint lightIndex = i.x;
    if(lightIndex >= ActiveLightCount)
        return;

    float4 posInWorld =  float4( Lights[lightIndex].position, 1);
    float4 posInClipSpace= mul(posInWorld, WorldToClipSpace);    
    posInClipSpace.xyz /= posInClipSpace.w;

    float2 uv = (posInClipSpace ) /2 + 0.5;
    uv.y = 1 - uv.y;

    float depth = DepthTexture.SampleLevel(texSampler, uv, 0);    
    float result = depth;

    float n = Near;
    float f = Far;
    
    float z =(-f * n) / (depth * (f - n) - f);
    
    float4 posInCam = mul( posInWorld,  WorldToCamera);
    result = z + posInCam.z;

    if(
        uv.x < 0 || uv.x > 1 ||
        uv.y < 0 || uv.y > 1 ||
        posInCam.z > 0
    )
        result =0;

    OutputTexture[i.xy] = result;
}
