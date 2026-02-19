// UpsampleAddPS.hlsl
// Samples a lower-resolution texture using full-resolution UVs (bilinear upsample),
// applies intensity, and outputs for additive blending.

cbuffer CompositeParams : register(b0)
{
    float2 InvTargetSize;
    float2 InvSourceSize;
    float4 PassColor;
    float PassIntensity;
};

Texture2D LowResTexture : register(t0);
SamplerState LinearSampler : register(s0);

struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4 psMain(PS_INPUT input) : SV_Target
{
    float4 color = LowResTexture.SampleLevel(LinearSampler, input.uv, 0); // Use mip level 0
    return color * PassIntensity * PassColor;
}
