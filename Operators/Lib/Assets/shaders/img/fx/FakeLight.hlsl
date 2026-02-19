//#include "shared/hash-functions.hlsl"
#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float Specularity;
    float Shade;
    float Twist;
    float SampleRadius;
    float2 Offset;
    float Amount;
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

Texture2D<float4> DisplaceMap : register(t0);
Texture2D<float4> Image : register(t1);
sampler texSampler : register(s0);


float IsBetween( float value, float low, float high) {
    return (value >= low && value <= high) ? 1:0;
}


float4 psMain(vsOutput psInput) : SV_TARGET
{   
    float2 uv = psInput.texCoord;
    float4 uvImage = DisplaceMap.Sample(texSampler, uv);
    float displaceMapWidth, displaceMapHeight;
    DisplaceMap.GetDimensions(displaceMapWidth, displaceMapHeight);
    displaceMapWidth = TargetWidth;
    displaceMapHeight = TargetHeight;

    float2 sampleOffset = SampleRadius / float2(displaceMapWidth, displaceMapHeight);

    float4 cx1= DisplaceMap.Sample(texSampler,  float2(uv.x + sampleOffset.x, uv.y));
    float4 cx2= DisplaceMap.Sample(texSampler,  float2(uv.x - sampleOffset.x, uv.y)); 
    float4 cy1= DisplaceMap.Sample(texSampler, float2(uv.x,       uv.y + sampleOffset.y));
    float4 cy2= DisplaceMap.Sample(texSampler, float2(uv.x,       uv.y - sampleOffset.y));    

    float x1= (cx1.r + cx1.g + cx1.b) / 3;
    float x2= (cx2.r + cx2.g + cx2.b) / 3;
    float y1= (cy1.r + cy1.g + cy1.b) / 3;
    float y2= (cy2.r + cy2.g + cy2.b) / 3;

    float2 d = float2( (x1-x2) , (y1-y2));
    d-= Offset/10;


    float angle = (d.x == 0 && d.y==0) ? 0 :  atan2(d.x, d.y) + Twist / 180 * 3.14158;
    float2 direction = float2( sin(angle), cos(angle));

    float len = length(d);
    float2 uv2 = 0.5 -direction * len * 10* Specularity;
    float4 cc= Image.Sample(texSampler,  uv2);
    cc.rgb = lerp(uvImage.rgb,  cc.rgb, Shade * Amount );
    cc.a *= uvImage.a;
    return float4( clamp(cc, float4(0,0,0,0) , float4(100,100,100,1)));
}