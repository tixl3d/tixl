#include "shared/hash-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer Params : register(b0)
{
    float2 Offset;
    float A;
    float B;

    float C;
    float D;
    float2 GainAndBias;
}

cbuffer Params : register(b1)
{
    int TargetWidth;
    int TargetHeight;
}


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
Texture2D<float4> ImageB : register(t1);
Texture2D<float4> Gradient : register(t2);

sampler Sampler : register(s0);
sampler ClampedSampler : register(s1);
sampler CustomSampler : register(s2);

static const float2 Center=Offset;
static const Texture2D<float4> Image=ImageA;
static const Texture2D<float4> Image2=ImageB;

//- DEFINES ------------------------------------
/*{defines}*/
//----------------------------------------------

float Biased(float f){return ApplyGainAndBias(f, GainAndBias);}
float4 SampleGradient(float f){return Gradient.SampleLevel(ClampedSampler, float2(f, 0.5), 0);}

float4 psMain(vsOutput input) : SV_TARGET
{

    float width, height;
    ImageA.GetDimensions(width, height);
    float4 c=float4(1,1,1,1);
   
    float2 uv = input.texCoord;
    int2 TargetSize=int2(TargetWidth,TargetHeight);
    int2 PixelCoord=int2(round(uv*TargetSize-.25));
    
{
//- METHOD -------------------------------------
/*{method}*/
//----------------------------------------------
}

    return c;
}