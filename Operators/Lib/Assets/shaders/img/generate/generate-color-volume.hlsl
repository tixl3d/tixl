#include "shared/noise-functions.hlsl"

RWTexture3D<float4> outputTexture : register(u0);

cbuffer Params : register(b0)
{
    float seed;
}

[numthreads(8,8,8)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint width, height, depth;
    outputTexture.GetDimensions(width, height, depth);
    float3 color = float3(i)/float3(width, height, depth);

    outputTexture[i.xyz] = float4(color, 1);
}
