#include "shared/color-functions.hlsl"

Texture2D<float4> InputTexture : register(t0);
sampler texSampler : register(s0);



cbuffer TimeConstants : register(b2)
{
    int Mode;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

#define Mode_RgbToOKLab 0
#define Mode_OKLabToRgb 1
#define Mode_RgbToLCh 2
#define Mode_LChToRgb 3

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 c = InputTexture.SampleLevel(texSampler, uv, 0.0);

    if(Mode == Mode_RgbToOKLab) 
    {
        return float4(RgbToOkLab(c.rgb),c.a); 
    }

    if(Mode == Mode_OKLabToRgb) 
    {
        return float4(OklabToRgb(c.rgb), c.a); 
    }

    if(Mode == Mode_RgbToLCh) 
    {
        return float4(RgbToLCh(c.rgb), c.a);
    }

    if(Mode == Mode_LChToRgb) 
    {
        return float4(LChToRgb(c.rgb), c.a);
    }

    return c;
}
