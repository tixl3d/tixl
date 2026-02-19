RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> Image : register(t0);
Texture2D<float4> DisplacementMap : register(t1);
sampler texSampler : register(s0);


cbuffer ParamConstants : register(b0)
{
    float SampleRadius;
    float Displacement;
    float DisplaceOffset;
    float SampleCount;
    float ShiftX;
    float ShiftY;
    float Angle;
}

[numthreads(16,16,1)]
void main(uint3 input : SV_DispatchThreadID)
{    
    uint width, height;
    outputTexture.GetDimensions(width, height);

    float2 uv = (float2)input.xy/ float2(width - 1, height - 1);
    float2 uv2= uv+ float2(ShiftX, ShiftY);
    float4 ccc = Image.SampleLevel(texSampler, uv, 0.0);
   
    float sx = SampleRadius / width;
    float sy = SampleRadius / height;
    
    float4 cy1= DisplacementMap.SampleLevel(texSampler, float2(uv2.x,       uv2.y + sy), 0.0);
    float4 cy2= DisplacementMap.SampleLevel(texSampler, float2(uv2.x,       uv2.y - sy),0.0);
    
    float4 cx1= DisplacementMap.SampleLevel(texSampler,  float2(uv2.x + sx, uv2.y),0.0);
    float4 cx2= DisplacementMap.SampleLevel(texSampler,  float2(uv2.x - sx, uv2.y),0.0); 
    float4 c =  DisplacementMap.SampleLevel(texSampler, float2(uv2.x,      uv2.y),0.0); 

    float cc= (c.r+ c.g +c.b);
    float x1= (cx1.r + cx1.g + cx1.b) / 3;
    float x2= (cx2.r + cx2.g + cx2.b) / 3;
    float y1= (cy1.r + cy1.g + cy1.b) / 3;
    float y2= (cy2.r + cy2.g + cy2.b) / 3;

    
    float2 d = float2( (x1-x2) , (y1-y2));
    float len = length(d);
    float a = length(d) ==0 ? 0 :  atan2(d.x, d.y) + Angle / 180 * 3.14158;

    float2 direction = float2( sin(a), cos(a));
    float2 p2 = direction * (Displacement * len + DisplaceOffset) * float2(height/ height, 1);
    
    
    float4 t1= float4(0,0,0,0);
    for(float i=-0.5; i< 0.5; i+= 1.0/ abs(SampleCount)) 
    {    
        t1+=Image.SampleLevel(texSampler, uv + p2 * i,0.0); 
    }    

    //c.r=1;
    float4 c2=t1/SampleCount;
    c2.a = clamp( c2.a, 0.00001,1);
    outputTexture[input.xy] = c2;
}
