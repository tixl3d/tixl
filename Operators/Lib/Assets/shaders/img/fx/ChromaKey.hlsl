// RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> Image : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 KeyColor;
    float4 Background;
    float Exposure;

    float WeightHue;
    float WeightSaturation;
    float WeightBrightness;
    float Amplify;
    float Mode;
    float ChokeRadius;
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

#define mod(x, y) (x - y * floor(x / y))


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
static float PI = 3.141578;




float HueDistance2(float hue1, float hue2)
{
    // Compute the absolute hue distance
    float hueDistance = abs(hue1 - hue2);

    // Normalize the hue distance to the range [0, 1]
    return min(hueDistance, 1.0 - hueDistance);
}


float GetColorDistance(float4 c)
{
    float3 hsb = float3(rgb2hsb( saturate( c.rgb)));

    float3 keyColorHsb = rgb2hsb(KeyColor.rgb);
    float3 weights = float3(smoothstep(0, 1, hsb.y * 10) * WeightHue, WeightSaturation, WeightBrightness);    
    float distance = saturate(
        length(float3(
            abs(HueDistance2(hsb.x, keyColorHsb.x)), // Hue
            (hsb.yz - keyColorHsb.yz) // Saturation and Brightness
            ) * weights) * Exposure - Amplify);
    return distance;
}



float4 psMain(vsOutput psInput) : SV_TARGET
{
    float width, height;
    Image.GetDimensions(width, height);

    float sx = ChokeRadius / width;
    float sy = ChokeRadius / height;

    float2 uv = psInput.texCoord;
    float4 c = Image.SampleLevel(texSampler, uv, 0.0);

    float distanceCenter = GetColorDistance(c);

    float4 y1 = GetColorDistance(Image.Sample(texSampler, float2(uv.x, uv.y + sy)));
    float4 y2 = GetColorDistance(Image.Sample(texSampler, float2(uv.x, uv.y - sy)));
    float4 x1 = GetColorDistance(Image.Sample(texSampler, float2(uv.x + sx, uv.y)));
    float4 x2 = GetColorDistance(Image.Sample(texSampler, float2(uv.x - sx, uv.y)));

    float distance = min(distanceCenter, min(min(y1, y2), min(x1, x2)));

    if (Mode < 0.5)
    {
        return float4(c.rgb, saturate(distance * c.a));
    }

    if (Mode < 1.5)
    {
        return lerp( lerp(c.rgba,Background, c.a) , c.rgba, distance);
    }

    if (Mode < 2.5)
    {
        return lerp(1, Background, distance);
    }

    return lerp(
        float4(c.rgb, saturate(1 - distance * c.a)),
        lerp(Background, c, saturate(1 - distance * c.a)),
        Background.a);
}
