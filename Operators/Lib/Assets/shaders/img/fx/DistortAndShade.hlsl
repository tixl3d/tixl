cbuffer ParamConstants : register(b0)
{
    float4 ShadeColor;
    float Displacement;
    float Shade;
    float2 Center;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
Texture2D<float4> ImageB : register(t1);
sampler texSampler : register(s0);


float IsBetween( float value, float low, float high) {
    return (value >= low && value <= high) ? 1:0;
}


float4 psMain(vsOutput psInput) : SV_TARGET
{        
    float2 uv = psInput.texCoord;
    float2 d = uv - Center;
    float4 displaceAmount =  ImageB.Sample(texSampler, float2(uv.x,      uv.y)); 
    float2 uv2= uv+ d* displaceAmount * Displacement;
    
    // float cc= (c.r+ c.g +c.b);
    // float x1= (cx1.r + cx1.g + cx1.b) / 3;
    // float x2= (cx2.r + cx2.g + cx2.b) / 3;
    // float y1= (cy1.r + cy1.g + cy1.b) / 3;
    // float y2= (cy2.r + cy2.g + cy2.b) / 3;

    
    // float2 d = float2( (x1-x2) , (y1-y2));
    // float len = length(d);
    // float a = length(d) ==0 ? 0 :  atan2(d.x, d.y) + Angle / 180 * 3.14158;

    // float2 direction = float2( sin(a), cos(a));
    // float2 p2 = direction * (Displacement * len + DisplaceOffset) * float2(height/ height, 1);
    
    float4 c2= ImageA.Sample(texSampler, uv2); 

    return lerp(c2, ShadeColor, Shade * displaceAmount );
}