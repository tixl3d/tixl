// A shader to combine three images into the R,G,B,A channels of a new image.
// Thomas Helzle - Screendream.de 2022 

cbuffer ParamConstants : register(b0)
{
    float IsRoughnessConnected;
    float IsMetallicConnected;
    float IsOcclusionConnected;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageRoughness : register(t0);
Texture2D<float4> ImageMetallic : register(t1);
Texture2D<float4> ImageOcclusion : register(t2);
Texture2D<float4> RemapCurves : register(t3);

sampler texSampler : register(s0);
sampler clampedSampler : register(s1);


float4 psMain(vsOutput psInput) : SV_TARGET
{    
    //return (IsOcclusionConnected > 0.5) ? float4(1,1,1,1) : float4(0,0,1,1);
    // return ImageOcclusion.Sample(texSampler, psInput.texCoord);
    float roughness = (IsRoughnessConnected > 0.5) ? pow( ImageRoughness.Sample(texSampler, psInput.texCoord).r,1) : 0.5f; 
    float metallic = (IsMetallicConnected > 0.5) ? ImageMetallic.Sample(texSampler, psInput.texCoord).g : 0.0f; 
    float occlusion = (IsOcclusionConnected > 0.5) ? ImageOcclusion.Sample(texSampler, psInput.texCoord).r : 1.0f; 
    roughness = RemapCurves.Sample(clampedSampler, float2(roughness,0.25f)).r;

    return float4(roughness,metallic,occlusion,1);
}
