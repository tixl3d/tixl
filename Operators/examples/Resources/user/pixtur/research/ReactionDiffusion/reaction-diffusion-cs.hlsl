#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"

cbuffer Params : register(b0)
{
    float DiffusionRateA;
    float DiffusionRateB;
    float FeedRate;
    float KillRate;
    float ReactionSpeed;
    float Counter;
    float RFx_FeedWeight;
    float GFx_KillWeight;
    float BFx_FillWeight;
    float Zoom;
}





sampler texSampler : register(s0);
Texture2D<float4> InputTexture : register(t0);
Texture2D<float4> FxTexture : register(t1);

RWTexture2D<float4> WriteOutput  : register(u0); 



float3 laplacian(in float2 uv, in float2 texelSize) {
  float3 rg = float3(0, 0, 0);

  rg += InputTexture.SampleLevel(texSampler, uv + float2(-1, -1)*texelSize,0).rgb * 0.05;
  rg += InputTexture.SampleLevel(texSampler, uv + float2(-0, -1)*texelSize,0).rgb * 0.2;
  rg += InputTexture.SampleLevel(texSampler, uv + float2(1, -1)*texelSize,0).rgb * 0.05;
  rg += InputTexture.SampleLevel(texSampler, uv + float2(-1, 0)*texelSize,0).rgb * 0.2;
  rg += InputTexture.SampleLevel(texSampler, uv + float2(0, 0)*texelSize,0).rgb * -1;
  rg += InputTexture.SampleLevel(texSampler, uv + float2(1, 0)*texelSize,0).rgb * 0.2;
  rg += InputTexture.SampleLevel(texSampler, uv + float2(-1, 1)*texelSize,0).rgb * 0.05;
  rg += InputTexture.SampleLevel(texSampler, uv + float2(0, 1)*texelSize,0).rgb * 0.2;
  rg += InputTexture.SampleLevel(texSampler, uv + float2(1, 1)*texelSize,0).rgb * 0.05;
				
  return rg;
}




[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{   
    // if(i.y < 30 && i.x == float(Counter % 100)) {
    //         WriteOutput[i.xy] = float4(1,1,1,1);
    // }

    float height, width;
    InputTexture.GetDimensions( width, height);

    float2 texelSize = float2(1./width, 1./height);

    float2 pos = float2(i.x, i.y) * texelSize + texelSize*0.5;
    float2 offset =  -((pos - 0.5) * 1) * texelSize * Zoom;
    //float2 offset = float2(0, 0.0002);
    pos += offset; 

    //float2 pos = float2( (float)i.x / width, (float)i.y / height);

    //float2 pos = psInput.texCoord;
    float4 col = InputTexture.SampleLevel(texSampler, pos  , 0);
    float a = col.r;
    float b = col.g;


    float4 fx = FxTexture.SampleLevel(texSampler, pos ,0);


    float3 lp = laplacian(pos   + offset, texelSize);

    float feed = FeedRate + ( fx.r  ) * RFx_FeedWeight/100;
    float kill = KillRate + ( fx.g ) * GFx_KillWeight/100;

    //float feed = FeedRate * fx.r;
    //float kill = KillRate * fx.g;

    float a2 = a + (DiffusionRateA * lp.x - a*b*b + feed*(1 - a)) * ReactionSpeed;
    float b2 = b + (DiffusionRateB * lp.y + a*b*b - (kill  + feed)*b) * ReactionSpeed;

    b2+=fx.b * BFx_FillWeight / 100;
    //a2+=fx.b * 0.4;

    col.r=saturate(a2);
    col.g=saturate(b2);
    col.b=0;
    col.a=1;
    //col = 0;
    WriteOutput[i.xy] = col;
}