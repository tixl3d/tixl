cbuffer ParamConstants : register(b0)
{
    float4 Color;
    float SampleRadius;
    float Strength;
    float Contrast;
    float MixOriginal;
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
sampler texSampler : register(s0);


float4 psMain(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;
    
    float width, height;
    Image.GetDimensions(width, height);
    
    float sx = SampleRadius / width;
    float sy = SampleRadius / height;
    
    float4 y1= Image.Sample(texSampler, float2(uv.x,       uv.y + sy));
    float4 y2= Image.Sample(texSampler, float2(uv.x,       uv.y - sy));
    
    float4 x1= Image.Sample(texSampler,  float2(uv.x + sx, uv.y));
    float4 x2= Image.Sample(texSampler,  float2(uv.x - sx, uv.y)); 
    float4 m =  Image.Sample(texSampler, float2(uv.x,      uv.y)); 
    
    float average =  (           
                    abs(x1.r-m.r) + abs(x2.r-m.r) + abs(y1.r - m.r) +abs(y2.r - m.r) +
                    abs(x1.g-m.g) + abs(x2.g-m.g) + abs(y1.g - m.g) +abs(y2.g - m.g) +
                    abs(x1.b-m.b) + abs(x2.b-m.b) + abs(y1.b - m.b) +abs(y2.b - m.b)
                ) * Strength + Contrast;
                

    float4 edgeColor = clamp(float4(average,average,average,1),0 , 10000) * Color;
    return lerp(edgeColor, m, MixOriginal);
}