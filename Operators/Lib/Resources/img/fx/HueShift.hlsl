// RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> inputTexture : register(t0);
Texture2D<float4> inputFxTexture : register(t1);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float Hue;
    float Saturation;
    float Exposure;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

#define mod(x, y) (x - y * floor(x / y))

float3 hsb2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z < 0.5 ?
                     // float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
               c.z * 2 * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y)
                     : lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), lerp(c.y, 0, (c.z * 2 - 1)));
}

float3 rgb2hsb(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(
        abs(q.z + (q.w - q.y) / (6.0 * d + e)),
        d / (q.x + e),
        q.x * 0.5);
}


float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 c = inputTexture.SampleLevel(texSampler, uv, 0.0);
    float4 fx = inputFxTexture.SampleLevel(texSampler, uv, 0.0);

    float a = saturate(c.a);
    c.rgb = clamp(c.rgb, 0.000001, 1000);
    c.a = saturate(c.a);

    float3 hsb = rgb2hsb(c.rgb);

    // Exposure
    hsb.z *= Exposure;

    // Shift Hue
    float hueShift = Hue + fx.g;
    hsb.x = mod((hsb.x + hueShift / 1), 1);

    // Adjust saturation
    hsb.y = saturate(hsb.y * Saturation);
  
    c.rgb = hsb2rgb(hsb);

    return c;
}
