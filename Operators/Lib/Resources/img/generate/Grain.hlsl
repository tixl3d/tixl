#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float Amount;
    float Color;
    float Exponent;
    float Brightness;
    float Time;
    float Scale;
}

/*cbuffer TimeConstants : register(b1)
{
//     float globalTime;
//     float time;
//     float runTime;
//     float beatTime;
}*/

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

#define mod(x, y) (x - y * floor(x / y))
float IsBetween( float value, float low, float high) {
    return (value >= low && value <= high) ? 1:0;
}

float4 GetNoiseFromRandom(float2 uv) 
{
    // Animation
    float pxHash = hash12( uv * 431 + 111);
    float t = Time + pxHash;

    // Color Noise
    float4 hash1 = hash42(( uv * 431 + (int)t));
    float4 hash2 = hash42(( uv * 431 + (int)t+1));
    float4 hash = lerp(hash1,hash2, t % 1);

    float4 grayScale = (hash.r+hash.g+hash.b)/3;
    float4 noise = (lerp(grayScale, hash, Color) - 0.5) * 2;

    noise = noise < 0 
            ? -pow(-noise, Exponent)
            : pow(noise, Exponent);
    
    noise += Brightness ;
    return noise;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{   
    float2 uv = psInput.texCoord;
    float4 orgColor = ImageA.Sample(texSampler, uv);    
    if(Scale > 1) {
        float2 pixelStep = float2(1/TargetWidth, 1/TargetHeight);
        float2 offset = Scale * pixelStep;
        //float2 offset = Scale * step;
        float2 fraction = uv % offset;
        float4 n1 = GetNoiseFromRandom(uv - fraction + 0.001 * pixelStep);
        float4 n2 = GetNoiseFromRandom(uv - fraction + 0.004 * pixelStep + offset);
        
        float4 noise = lerp(n1, n2, 0);

        float4 color= float4(orgColor.rgb + noise.rgb * Amount, 1);
        return color;

    }
    else {
        float4 noise = GetNoiseFromRandom(uv);
        float4 color= float4(orgColor.rgb + noise.rgb * Amount, 1);
        return color;
    }
}