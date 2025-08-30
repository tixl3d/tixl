cbuffer ParamConstants : register(b0)
{
    float SampleRadius;
    float Strength;
    float Clamping;
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

// The below is mostly adapted from the Cables.gl Sharpen shader and converted to TiXL/HLSL.
// https://cables.gl
// Used with permission according to the MIT license: https://opensource.org/license/MIT

float desaturate(float4 color)
{
  return dot(float3(0.2126, 0.7152, 0.0722), color.xyz);
}

float4 psMain(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;
    
    float width, height;
    Image.GetDimensions(width, height);
    
    float pX = SampleRadius / width;
    float pY = SampleRadius / height;

    float4 col=float4(1.0,0.0,0.0,1.0);
    col = Image.Sample(texSampler, uv);
    
    
    float colorL = desaturate(Image.Sample(texSampler, uv + float2(-pX, 0) ));
    float colorR = desaturate(Image.Sample(texSampler, uv + float2( pX, 0) ));
    float colorA = desaturate(Image.Sample(texSampler, uv + float2( 0, -pY) ));
    float colorB = desaturate(Image.Sample(texSampler, uv + float2( 0, pY) ));
    
    float colorLA = desaturate(Image.Sample(texSampler, uv + float2(-pX, pY)));
    float colorRA = desaturate(Image.Sample(texSampler, uv + float2( pX, pY)));
    float colorLB = desaturate(Image.Sample(texSampler, uv + float2(-pX, -pY)));
    float colorRB = desaturate(Image.Sample(texSampler, uv + float2( pX, -pY)));
    
    float4 final = col + col * Strength * (8.0*desaturate(col) - colorL - colorR - colorA - colorB - colorLA - colorRA - colorLB - colorRB);
    if (Clamping > 0.0){
        final = saturate(final);
    }
    return final;
}

