#include "shared/color-functions.hlsl"

Texture2D<float4> InputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{    
    float Mode;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 c = InputTexture.SampleLevel(texSampler, uv, 0.0);

    if(Mode< 0.5) {
        return float4(RgbToOkLab(c.rgb),c.a);
    }

    if(Mode < 1.5) {
        return float4(OklabToRgb(c.rgb), c.a); 
    }

    if(Mode < 2.5) 
    {
        return float4(RgbToLCh(c.rgb), c.a);
    }

    if(Mode < 3.5) 
    {
        return float4(LChToRgb(c.rgb), c.a);
    }

    return c;
}
