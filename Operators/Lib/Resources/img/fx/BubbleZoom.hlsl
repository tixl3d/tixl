#include "shared/bias-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float2 Center;

    float ScaleFactor;
    float Feather;
    float Radius;
   // float Bias;
    float2 GainAndBias;
    float FlipEffect;
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

Texture2D<float4> ImageA : register(t0);
Texture2D<float4> Gradient : register(t1);
sampler texSampler : register(s0);

float fmod(float x, float y) {
    return (x - y * floor(x / y));
} 

float4 psMain(vsOutput psInput) : SV_TARGET
{    
    float2 uv = psInput.texCoord;

    float aspectRatio = TargetWidth/TargetHeight;
    float2 p = uv;
    p-= 0.5;
    p.x *=aspectRatio;
 
    float c = distance(p, Center*float2(1,-1)) * 2;

    float adjustedRadius = 2 * Radius * aspectRatio ;

    c+= -adjustedRadius + 2 * abs(Feather) / aspectRatio;
    
    c = saturate(c / Feather);

    float dBiased = ApplyGainAndBias(c, GainAndBias);

    dBiased = lerp(dBiased, 1.0 - dBiased, FlipEffect);

    float4 gradient = Gradient.Sample(texSampler, float2(dBiased, 0));
    
    float2 zoomedUV  = ((uv+Center*float2(ScaleFactor*.5,-ScaleFactor*.5)) - 0.5) / ScaleFactor + 0.5;
    
    float2 lookupUv = lerp(zoomedUV, uv,  dBiased);
    
    float4 orgColor = ImageA.Sample(texSampler, lookupUv);
    return float4(lerp(orgColor.rgb, gradient.rgb, gradient.a), orgColor.a);
}