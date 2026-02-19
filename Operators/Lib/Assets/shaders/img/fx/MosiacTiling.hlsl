#include "shared/hash-functions.hlsl"


cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float2 Stretch2;

    float Size;
    float SubdivisionThreshold;
    float Padding;
    float Feather;

    float4 GapColor;
    float MixOriginal;
    float MaxSubdivisions;

    float Randomize;
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
Texture2D<float4> FxImage : register(t1);
sampler texSampler : register(s0);


#define fmod(x, y) ((x) - (y) * floor((x) / (y)))

static const float stepOffset = 0.25;

float4 psMain(vsOutput input) : SV_TARGET
{
    float width, height;
    Image.GetDimensions(width, height);

    float aspectRatio = width/height;

    float2 uv = input.texCoord;
    float2 p = uv;
    p-= 0.5 + Center * (Size / float2(aspectRatio,1)) ;
    p.x *= aspectRatio;

    float2 pInCell = fmod(p, Size);
    float2 pCell = uv - pInCell / float2(aspectRatio,1);

    float currentSize = Size;    
    int steps = (int)clamp(MaxSubdivisions,1,7);

    float4 c1,c2,c3,c4;
    float2 uv1, uv2, uv3, uv4, avgUv;

    int step;
    for( step = 0; step<steps; ++step) 
    {
        uv1 = uv - (pInCell - (currentSize) * ( 0.5 + float2(-stepOffset, -stepOffset)))  / float2(aspectRatio,1);
        uv2 = uv - (pInCell - (currentSize) * ( 0.5 + float2( stepOffset,  stepOffset)))  / float2(aspectRatio,1);
        uv3 = uv - (pInCell - (currentSize) * ( 0.5 + float2( stepOffset, -stepOffset)))  / float2(aspectRatio,1);
        uv4 = uv - (pInCell - (currentSize) * ( 0.5 + float2(-stepOffset,  stepOffset)))  / float2(aspectRatio,1);
        
        c1 = FxImage.SampleLevel(texSampler, uv1, 0);
        c2 = FxImage.SampleLevel(texSampler, uv2, 0);

        c3 = FxImage.SampleLevel(texSampler, uv3, 0);
        c4 = FxImage.SampleLevel(texSampler, uv4, 0);

        avgUv = (uv1 + uv2) /2;
        float hash =  hash12( avgUv) ;

        if( step == steps -1 || 
            max(length(c1 - c2), length(c3 - c4) ) + hash * Randomize  < SubdivisionThreshold)
            break;

        currentSize /= 2;
        pInCell = fmod(pInCell , currentSize);
    }
    
    float4 imageColor = Image.SampleLevel(texSampler, (uv1 + uv2) /2, 0   );
    float2 pFromCenter = abs(pInCell / (currentSize ) -0.5 ) *2;
    float d = 1-max(pFromCenter.x, pFromCenter.y);
    float gapFactor = smoothstep(Padding - Feather, Padding + Feather, d / (step +1));
    float4 returnColor = lerp(1, imageColor, MixOriginal);
    return lerp(GapColor, returnColor, gapFactor);
}