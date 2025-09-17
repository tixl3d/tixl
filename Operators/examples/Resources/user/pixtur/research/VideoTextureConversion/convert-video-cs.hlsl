#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 Color;
}

Texture2D<float4> FxTexture : register(t0);

RWTexture2D<float4> WriteOutput  : register(u0); 

[numthreads(32,32,1)]
void main(uint3 i : SV_DispatchThreadID)
{   
    int texWidth;
    int texHeight;
    WriteOutput.GetDimensions(texWidth, texHeight);
    if(i.x >= texWidth || i.y >= texHeight){
        return;
    }

    float2 res= float2(texWidth, texHeight);

    float4 inputColor = FxTexture[i.xy];

    WriteOutput[i.xy] = inputColor * Color; 
}
