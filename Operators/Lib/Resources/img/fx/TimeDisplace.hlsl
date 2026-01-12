// #include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float DisplaceAmount;
    float DisplaceOffset;
    float Twist;
    float Shade;
}

cbuffer IntParameters : register(b2)
{
    int DisplaceMode;
    int UseRGSSMultiSampling;
    int SliceIndex;
    int ArrayLength;
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2DArray<float4> Image : register(t0);
Texture2D<float4> DisplaceMap : register(t1);
sampler texSampler : register(s0);

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;

    float4 c = 0;

    float4 rgba = DisplaceMap.SampleLevel(texSampler, uv, 0);
    int sliceOffset = (int)floor(((rgba.r + rgba.g + rgba.b) / 3.0) * DisplaceAmount + 0.5);

    int slice = (SliceIndex + sliceOffset) % ArrayLength;
    return Image.SampleLevel(texSampler, float3(uv, slice), 0);
}