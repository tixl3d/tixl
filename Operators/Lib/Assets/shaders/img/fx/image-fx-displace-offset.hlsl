//#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float DisplaceAmount;
    float DisplaceOffset;
    float Twist;
    float Shade;
    float SampleCount;
    float SampleRadius;
    float SampleSpread;
    float SampleOffset;
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

Texture2D<float4> Image : register(t0);
Texture2D<float4> DisplaceMap : register(t1);
sampler texSampler : register(s0);


float IsBetween( float value, float low, float high) {
    return (value >= low && value <= high) ? 1:0;
}


float4 psMain(vsOutput psInput) : SV_TARGET
{   
    int samples = (int)clamp(SampleCount+0.5,1,32);
    float displaceMapWidth, displaceMapHeight;
    DisplaceMap.GetDimensions(displaceMapWidth, displaceMapHeight);

    float2 uv = psInput.texCoord;

    //return float4(smoothstep(DisplaceAmount, DisplaceAmount, uv.x*10), 0,0,1);

    //float4 ccc = Image.Sample(texSampler, uv);

    float4 displacement= DisplaceMap.Sample(texSampler, uv);

    // float subHashX = hash12(psInput.texCoord * 100 + (beatTime * 10.1 % 10.1)); 
    // float subHashY = hash12(psInput.texCoord * 101 + (beatTime * 101.013 % 12.1)); 
    // float4 hash = hash42((psInput.texCoord + float2(subHashX,subHashY)) 
    //     + float2( 1233+ (beatTime * 0.001 % 13.1), 
    //         3000+ (beatTime * 0.001 % 13.1) )); 
    //return float4(subHashX, subHashY, 0,1);
   
    
    // float4 cx1,cx2, cy1, cy2;
    // cx1=cx2=cy2=cy1= float4(0,0,0,0);
    // int dSamples=1;
    // float sx = SampleRadius / displaceMapWidth;
    // float sy = SampleRadius / displaceMapHeight;
    // int sampleIndex = 1;
    // //float2 uv2 = uv + float2(subHashX, subHashY)*0.00;
    // //for(int sampleIndex = 1; sampleIndex < 4; sampleIndex++) {
    //     cx1+= DisplaceMap.Sample(texSampler, float2(uv.x + sx*sampleIndex, uv.y));
    //     cx2+= DisplaceMap.Sample(texSampler, float2(uv.x - sx*sampleIndex, uv.y)); 
    //     cy1+= DisplaceMap.Sample(texSampler, float2(uv.x,       uv.y + sy*sampleIndex));
    //     cy2+= DisplaceMap.Sample(texSampler, float2(uv.x,       uv.y - sy*sampleIndex));    
    //}
    // cx1 /= dSamples;
    // cx2 /= dSamples;
    // cy1 /= dSamples;
    // cy2 /= dSamples;

    // float x1= (cx1.r + cx1.g + cx1.b) / 3;
    // float x2= (cx2.r + cx2.g + cx2.b) / 3;
    // float y1= (cy1.r + cy1.g + cy1.b) / 3;
    // float y2= (cy2.r + cy2.g + cy2.b) / 3;

    float2 d = float2(displacement.x ,0);

    float a = (d.x == 0 && d.y==0) ? 0 :  atan2(d.x, d.y) + Twist / 180 * 3.14158;
    
    float2 direction = float2( sin(a), cos(a));

    float len = length(d);
    // float4 cc= Image.Sample(texSampler, -direction * len*3 + 0.5);
    // cc.rgb *= (1-len*Shade*100);
    //return cc;


    float2 p2 = direction * (-DisplaceAmount * len * 10 + DisplaceOffset);// * float2(height/ height, 1);
    float imgAspect = TargetWidth/TargetHeight;
    p2.x /=imgAspect;
    
    //return float4(direction, 0,1);
    
    float4 t1= float4(0,0,0,0);
    for(float i=-0.5; i< 0.5; i+= 1.0001/ samples) 
    {    
        t1+=Image.Sample(texSampler, uv + p2 * (i*SampleSpread +1-SampleOffset)); 
    }    

    //c.r=1;
    float4 c2=t1/samples;
    c2.rgb *= (1-len*Shade*100);
    c2.a = clamp( c2.a, 0.00001,1);
    return c2;
}