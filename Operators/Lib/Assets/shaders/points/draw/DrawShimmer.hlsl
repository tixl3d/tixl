#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/hash-functions.hlsl"

static const float4 Corners[] = 
{
    //   px py  u v
  float4(-1, -1, 0,1),
  float4( 1, -1, 1,1), 
  float4( 1,  1, 1,0), 
  float4( 1,  1, 1,0), 
  float4(-1,  1, 0,0), 
  float4(-1, -1, 0,1),  
};

cbuffer Params : register(b0)
{
    float Style;
    float Complexity;
    float Gamma;

    float ScatterDistribution;
    float ScatterLength;
    float ScatterBrightness;
    float Colorize;

    float CoreBrightness;
    float CircularCompletion;
    float CircularCompletionEdge;
    float Time;

    float UseRGSSMultiSampling;

    float TargetWidth;
    float TargetHeight;

    float CompletionAffectsLength;
};

cbuffer Transforms : register(b1)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
    float spriteIndex : COLOR1;
    float rotation: ROTATION;
};


struct Sprite
{
    float2 PosInClipSpace;
    float2 Size;
    float Rotation;
    float4 Color;
    float2 UvMin;
    float2 UvMax;
    float3 __padding;
};

sampler WrappedSampler : register(s0);
sampler ClampedSampler : register(s1);

StructuredBuffer<Sprite> Sprites : t0;
Texture2D<float4> NoiseImage : register(t1);
Texture2D<float4> Gradient : register(t2);

static const float ShimmerStyle = 0;
static const float SparkleStyle = 1;
static const float TextureStyle = 2;

float3 hsb2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z < 0.5 ?
        //float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
        c.z*2 * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y)
        : lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), lerp(c.y, 0, (c.z * 2 - 1) ) );
}




psInput vsMain(uint id: SV_VertexID)
{
    psInput output;
    float discardFactor = 1;
    int cornerIndex = id % 6;
    int particleId = id / 6;
    output.spriteIndex = particleId;
    float4 cornerFactors = Corners[cornerIndex];

    float4 aspect = float4(CameraToClipSpace[1][1] / CameraToClipSpace[0][0],1,1,1);    

    Sprite sprite = Sprites[particleId];

    float2 corner = float2(cornerFactors.x * sprite.Size.x, 
                          cornerFactors.y * sprite.Size.y);


    float imageRotationRad = (-sprite.Rotation - 90) / 180 * PI;     

    float sina = sin(-imageRotationRad - PI/2);
    float cosa = cos(-imageRotationRad - PI/2);

    corner = float2(
        cosa * corner.x - sina * corner.y,
        cosa * corner.y + sina * corner.x 
    );                              

    float2 p = float2( corner.x / aspect.x + sprite.PosInClipSpace.x,
                       corner.y + sprite.PosInClipSpace.y);

    output.position = float4(p, 0,1);
    output.rotation = sprite.Rotation;

    output.texCoord = lerp(sprite.UvMin, sprite.UvMax, cornerFactors.zw);
    output.color = sprite.Color;
    return output;    
}

float remap(float value, float inMin, float inMax, float outMin, float outMax) {
    float factor = (value - inMin) / (inMax - inMin);
    float v = factor * (outMax - outMin) + outMin;
    return v;
}

static const float SimmerComplexitFactor = 100;
static float _rotation = 0;


float4 ComputeShimmer(float2 p, float spriteIndex) {
    float2 Center = 0;

    float d = length(p) * 2;
    float angle = (atan2(p.x, p.y)) / (2*PI)+0.5;

    // Scatter distribution...
    float distributrionComplexity = Complexity / SimmerComplexitFactor;
    float angleForFx = (angle + _rotation/360 + 0.5) % 1;
    float2 noiseAPos = float2(angleForFx * distributrionComplexity +  spriteIndex*0.1, 
                             angleForFx*0.2 + Time*0.1 + spriteIndex * 0.2);

    float4 noiseA =  NoiseImage.SampleLevel(WrappedSampler, noiseAPos, 0.0) * 1;

    float distributionOffset = (noiseA.r - 0.5) * ScatterDistribution / Complexity*2;
    angleForFx += distributionOffset;
    angleForFx %= 1;

    // Noise for other effects
    float4 noiseB =  NoiseImage.SampleLevel(WrappedSampler, float2(
                        angleForFx * Complexity / SimmerComplexitFactor + spriteIndex * 0.2, 
                        angleForFx*0.3 + Time*-0.11 + spriteIndex * 0.13
                        ), 0.0);

    float brightness =  1-pow(d, 1+Gamma);
    
    // Completion
    float c2 = CircularCompletion/720;
    float cEdge = CircularCompletionEdge/720;
    float cc= smoothstep(c2+cEdge, c2, angle) + smoothstep(1-c2- cEdge, 1-c2, angle);    
    brightness *= lerp(1, (noiseB.b), ScatterBrightness) * cc;

    float completionRatio = CircularCompletion/360;
    brightness +=  pow( 1-d, lerp(50, 20, completionRatio) ) * CoreBrightness * pow(completionRatio,0.1);
    brightness = pow(brightness, Gamma);

    // Colorize
    float4 randomColor = float4(noiseB.rbg,1);
    randomColor.xyz /= length(randomColor.xyz);

    
    float4 colorOut = float4(brightness.xxx * lerp(1,randomColor.rgb, Colorize),1);

    float2 uv = float2(d + (noiseB.g - 0.5) * ScatterLength, 0.75);
    colorOut *= Gradient.Sample(ClampedSampler, uv);

    return clamp(colorOut,0,1000);
}




float4 ComputeSparkle(float2 p, float spriteIndex) {
    float2 Center = 0;

    float angle = (atan2(p.x, p.y)) / (2*PI)+0.5;

    float2 noisePos = float2(angle * Complexity/64 +  spriteIndex*0.1, 
                             angle*0.2 + Time*0.1 + spriteIndex * 0.2);

    float4 noiseA =  NoiseImage.SampleLevel(WrappedSampler, noisePos, 0.0) * 1;
    float noiseDistort = (noiseA.r - 0.5) * ScatterDistribution/Complexity;
    angle += noiseDistort;
    
    float c2 = CircularCompletion/720;
    float cEdge = CircularCompletionEdge/720;
    float cc= (smoothstep(c2+cEdge/2, c2-cEdge/2, angle) + smoothstep(1-c2- cEdge/2, 1-c2+cEdge/2, angle));
    
    // return float4(
    //     smoothstep(c2+cEdge/2, c2-cEdge/2, angle),
    //     smoothstep(1-c2- cEdge/2, 1-c2+cEdge/2, angle),
    //     0,1);

    float repeatFraction = 1/Complexity;

    float segments = angle * Complexity;
    float segmentF = segments %  1;
    float segmentIndex = segments - segmentF;

    float mappedGamma = 0.8- Gamma /2;

    float d = length(p) * 2;
    float constantLineWidthOffset =  0.5-  mappedGamma/d;
    float segmentFill = abs(segmentF - 0.5) * 2 + constantLineWidthOffset;

    float filled = smoothstep(mappedGamma + 0.4, mappedGamma, segmentFill);

    float4 segmentNoise =  NoiseImage.SampleLevel(WrappedSampler, float2(
                         (segmentIndex / Complexity*173.1236) %1 + spriteIndex * 0.213 - Time*0.14, 
                         Time*0.2 + segmentIndex *0.14
                         ), 0.0);
                    
    float segmentD = (d + (segmentNoise.r-0.5) * ScatterLength) / lerp(1, cc, CompletionAffectsLength);
    

    float4 gradient= Gradient.Sample(ClampedSampler, float2(1-segmentD, 0.25));
    filled *= 1+ (segmentNoise.r - 0.5) * ScatterBrightness;

    // Colorize
    float4 randomColor = float4(segmentNoise.rbg,1);
    randomColor.xyz /= length(randomColor.xyz);
    gradient.xyz *= lerp(1, randomColor.xyz, Colorize);

    float4 colorOut = gradient * filled;
    colorOut.a *= cc;

    //return float4(randomColor.rgb,1) ;

    //d= gradient.r;    


    // float brightness = pow(1-d,3);
    // brightness *= lerp(1, (noiseB.b), ScatterBrightness) * cc;

    //float completionRatio = CircularCompletion/360;
    //brightness +=  pow(1-d, lerp(50, 20, completionRatio) ) * CoreBrightness * pow(completionRatio,0.1);

    // brightness = pow(brightness, Gamma);
    // float4 colorOut = float4(brightness.xxx * lerp(1,noiseB.rgb, Colorize),1);
    // colorOut *= float4(1,0,0,1);
    //float4 colorOut = float4(x.xxx,1);
    return clamp(colorOut,0,1000);
}


float4 psMain(psInput input) : SV_TARGET
{
    float2 p = input.texCoord;
    p -= 0.5;

    _rotation = input.rotation;
    //return float4(_rotation/360, 0,0,1);

    float4 colorOut =0;
    float d = length(p) * 2;
    if(d > 1)
        return 0;

    if(Style < 0.5) 
    {
        //float4 gradient= Gradient.SampleLevel(WrappedSampler,1-d,0);
        //d= gradient.r;

        if(UseRGSSMultiSampling) 
        {
            // 4x rotated grid
            float4 offsets[2];
            offsets[0] = float4(-0.375, 0.125, 0.125, 0.375);
            offsets[1] = float4(0.375, -0.125, -0.125, -0.375);
            
            float2 sxy = float2(TargetWidth, TargetHeight);
            
            float4 colorOut4 = ComputeShimmer(p + offsets[0].xy / sxy, input.spriteIndex)+
                            ComputeShimmer(p + offsets[0].zw / sxy, input.spriteIndex)+
                            ComputeShimmer(p + offsets[1].xy / sxy, input.spriteIndex)+
                            ComputeShimmer(p + offsets[1].zw / sxy, input.spriteIndex);

            float4 colorOutB = colorOut4 /4 * input.color;
            return clamp(float4(colorOutB.rgb, colorOutB.a), 0, float4(1000,1000,1000,1));
        }

        colorOut = ComputeShimmer(p, input.spriteIndex) * input.color;
    }
    else {
        float4 gradient= Gradient.SampleLevel(WrappedSampler,1-d,0);
        d= gradient.r;

        if(UseRGSSMultiSampling) 
        {
            // 4x rotated grid
            float4 offsets[2];
            offsets[0] = float4(-0.375, 0.125, 0.125, 0.375);
            offsets[1] = float4(0.375, -0.125, -0.125, -0.375);
            
            float2 sxy = float2(TargetWidth, TargetHeight);
            
            float4 colorOut4 = ComputeSparkle(p + offsets[0].xy / sxy, input.spriteIndex)+
                            ComputeSparkle(p + offsets[0].zw / sxy, input.spriteIndex)+
                            ComputeSparkle(p + offsets[1].xy / sxy, input.spriteIndex)+
                            ComputeSparkle(p + offsets[1].zw / sxy, input.spriteIndex);

            float4 colorOutB = colorOut4 * input.color /4;
            return clamp(float4(colorOutB.rgb, colorOutB.a), 0, float4(1000,1000,1000,1));
        }

        colorOut = ComputeSparkle(p, input.spriteIndex) * input.color;
    }

    return clamp(float4(colorOut.rgb, colorOut.a), 0, float4(1000,1000,1000,1));
}
