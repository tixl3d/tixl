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

    /*float2 uv = (float2)i.xy / float2(width - 1, height - 1);*/
    /*uv = uv*2.0 - 1.0;*/
    /*float l = length(uv);*/
    /*uv *= strength * sin(l*time*speed);*/
    /*uv = uv*0.5 + 0.5;*/


    float sum = 0.0;
    const int NUM_OCTAVES = 9;
    for (int j = 0; j < NUM_OCTAVES; j++)
    {
        float s = 1.0 / float(1 << j);
        float c = snoise((float3(i.xyz) + float3(seed, seed, seed))* s).x;
        sum += c * 1.0/float(256 >> j);
    }

    // float3 p = float3(i) - float(width)/2.0;
    // if (length(p) > 96)
    //     sum = 0;
    outputTexture[i.xyz] = float4(sum, sum, sum, 1);
}
