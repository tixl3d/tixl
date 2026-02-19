cbuffer ParamConstants : register(b0)
{

    float Size;
    float Strength;
    float SampleCount;
    float Distort;
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
    uv -= 0.5;
    uv *= 0.95;
    uv += 0.5;

    // Lens distortion
    float2 dir = uv - 0.5;
    
    uv += dir * dot(dir, dir) * Distort;
    
    float4 col = 0;

    int samples = clamp(SampleCount, 3,20);

    float4 centerColor = Image.Sample(texSampler, uv);
    float2 offset = float2(1.0, 1.0) * 0.01 * dir * Size;

    float x = 0;
    for(int i = 1; i < samples; i++) 
    {
        float f= (float)(i - 0.5) / samples;
        x+= 0.5;

        float4 left = Image.Sample(texSampler, uv - offset * f);
        float4 right = Image.Sample(texSampler, uv + offset * f);    
        col.r += left.r * 0.5;
        col.b += right.b * 0.5;

        float4 left2 = Image.Sample(texSampler, uv - offset * f/2);
        float4 right2 = Image.Sample(texSampler, uv + offset * f/2);    
        col.ga += left2.ga * 0.25;
        col.ga += right2.ga * 0.25;        
    }
    col.rgb /= x;

    return clamp(float4(lerp(centerColor.rgb, col.rgb, Strength), col.a), 0, float4(1000,1000,1000,1));

}