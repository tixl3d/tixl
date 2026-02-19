#include "shared/blend-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float2 Stretch;
    float2 Position;
    float Scale;
    float Feather;
    float GradientBias;
    float Rotate;
    float BlendMode;
    float IsTextureValid;
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

Texture2D<float4> ImageA : register(t0);
sampler texSampler : register(s0);



float sdBox( in float2 p, in float2 b )
{
    float2 d = abs(p)-b;
    return length(
        max(d,float2(0,0))) + min(max(d.x,d.y), 
        0.0);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth/TargetHeight;

    float2 p = psInput.texCoord;
    //p.x -= 0.5;
    p -= 0.5;
    p.x *= aspectRatio;

    // Rotate
    float imageRotationRad = (-Rotate - 90) / 180 *3.141578;     

    float sina = sin(-imageRotationRad - 3.141578/2);
    float cosa = cos(-imageRotationRad - 3.141578/2);

    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x 
    );
    float2 pRotated = p;
    p /= Stretch;
    p-=Position * float2(1,-1);    
    float d= length(p);    
    float f=Feather * Scale/2;

    d = smoothstep(Scale/2 - f, Scale/2 + f, d);

    float dBiased = GradientBias>= 0 
        ? pow( d, GradientBias+1)
        : 1-pow( clamp(1-d,0,10), -GradientBias+1);

    float4 c= lerp(Fill, Background,  dBiased);
    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
    float a = clamp(orgColor.a + c.a - orgColor.a*c.a, 0,1);

    return (IsTextureValid < 0.5) ? c : BlendColors(orgColor, c, (int)BlendMode);
}