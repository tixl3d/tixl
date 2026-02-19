cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float4 LineColor;
    float2 Center;
    float Width;
    float Rotation;
    float LineThickness;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

cbuffer TimeConstants : register(b2)
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

float4 psMain(vsOutput psInput) : SV_TARGET
{    
    float aspectRatio = TargetWidth/TargetHeight;
    float2 p = psInput.texCoord;
    p.x /= aspectRatio;
    p -= float2(0.5 / aspectRatio, 0.5);

    // Show Center
    // if( length(p - Center) < 0.01) {
    //     return float4(1,1,0,1);
    // }

    float radians = Rotation / 180 *3.141578;
    float2 angle =  float2(sin(radians),cos(radians));

    float dist=  dot(p-Center, angle) / Width;



    if(dist < 0) {
        dist = -dist;
        angle *= -1;
    }

    float4 colorEffect = Fill;

    if(dist > 1 ) {
        p -= (dist - 1) * Width * angle;
        colorEffect = Background;
    }
    p += float2(0.5 / aspectRatio, 0.5);
    p.x *= aspectRatio;

    float line2= smoothstep(1,0, abs(1-dist)*1000*Width-LineThickness+1);
    colorEffect = lerp(colorEffect, LineColor, line2);
    return ImageA.Sample(texSampler, p) * colorEffect;
}