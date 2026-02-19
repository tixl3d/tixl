//#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float DiffusionRateA;
    float DiffusionRateB;
    float FeedRate;
    float KillRate;
    float ReactionSpeed;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

cbuffer Resolution : register(b2)
{
    float TargetWidth;
    float TargetHeight;
}


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageInput : register(t0);
Texture2D<float4> FxInput : register(t1);
sampler texSampler : register(s0);


// float IsBetween( float value, float low, float high) {
//     return (value >= low && value <= high) ? 1:0;
// }


float3 laplacian(in float2 uv, in float2 texelSize) {
  float3 rg = float3(0, 0, 0);

  rg += ImageInput.Sample(texSampler, uv + float2(-1, -1)*texelSize,0).rgb * 0.05;
  rg += ImageInput.Sample(texSampler, uv + float2(-0, -1)*texelSize).rgb * 0.2;
  rg += ImageInput.Sample(texSampler, uv + float2(1, -1)*texelSize).rgb * 0.05;
  rg += ImageInput.Sample(texSampler, uv + float2(-1, 0)*texelSize).rgb * 0.2;
  rg += ImageInput.Sample(texSampler, uv + float2(0, 0)*texelSize).rgb * -1;
  rg += ImageInput.Sample(texSampler, uv + float2(1, 0)*texelSize).rgb * 0.2;
  rg += ImageInput.Sample(texSampler, uv + float2(-1, 1)*texelSize).rgb * 0.05;
  rg += ImageInput.Sample(texSampler, uv + float2(0, 1)*texelSize).rgb * 0.2;
  rg += ImageInput.Sample(texSampler, uv + float2(1, 1)*texelSize).rgb * 0.05;
				
  return rg;
}




float4 psMain(vsOutput psInput) : SV_TARGET
{   
    float2 pos = psInput.texCoord;
    //pos += float2(0,0.001);
    float4 col = ImageInput.Sample(texSampler, pos);
    float a = col.r;
    float b = col.g;

    float height, width;
    ImageInput.GetDimensions( width, height);
    float2 texelSize = 1/float2(width,height) * 2.4;

    //return col;
    //return ImageInput.Sample(texSampler, pos + float2(0,0.01)) *0.0001;
    float4 fx = FxInput.Sample(texSampler,pos);


    float3 lp = laplacian(pos, texelSize);

    float feed = FeedRate * fx.r;
    float kill = KillRate * fx.g;

    float a2 = a + (DiffusionRateA * lp.x - a*b*b + feed*(1 - a)) * ReactionSpeed;
    float b2 = b + (DiffusionRateB * lp.y + a*b*b - (kill  + feed)*b) * ReactionSpeed;

    col.r=saturate(a2);
    col.g=saturate(b2);
    col.b=0;
    col.a=1;
    return col;
}