// RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 Colorize;
    float4 Background;
    float Exposure;
    float Contrast;
    float Saturation;
    float OrangeTeal;
    float2 PreventClamping;
    float Brightness;
    float Hue;
    float Vignette;
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
static float PI = 3.141578;

float SCurve(float value, float amount, float correction)
{
    float curve = (value < 0.5)
                      ? pow(value, amount) * pow(2.0, amount) * 0.5
                      : 1.0 - pow(1.0 - value, amount) * pow(2.0, amount) * 0.5;

    return pow(curve, correction);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 c = inputTexture.SampleLevel(texSampler, uv, 0.0);

    float a = saturate(c.a);
    c.rgb = clamp(c.rgb, 0.000001, 1000);
    c.a = saturate(c.a);

    float3 hsb = rgb2hsb(c.rgb);

    // Vignette
    float distanceFromCenter = length(float2(0.5, 0.5) - uv) * Vignette;
    hsb.z *= saturate(1 - distanceFromCenter);

    hsb.z *= Exposure;

    // Colorize
    // if(Colorize.a > 0) {
    //     float3 colorizeHsb = rgb2hsb(Colorize.rgb);
    //     float3 colorized = hsb2rgb(float3(colorizeHsb.x, colorizeHsb.y, hsb.z  ));
    //     float3 blendedColors = lerp( hsb2rgb(hsv) ,colorized  , Colorize.a );
    //     hsv = rgb2hsb(blendedColors);
    // }

    // Shift Hue
    hsb.x = mod((hsb.x + Hue / 360), 1);

    // Adjust saturation
    hsb.y = saturate(hsb.y * Saturation);

    // Prevent clamping (tone mapping)
    float power = 6;

    float clampingBlendRange = clamp(PreventClamping.x, 0.001, 1);
    if (hsb.z * 2 > 1 - clampingBlendRange)
    {

        float clampingBlendRange = clamp(PreventClamping.x, 0.001, 1);
        float pa = 1 - clampingBlendRange;
        float t = saturate((hsb.z * 2 - pa) / (PreventClamping.y - 1 + clampingBlendRange));

        t = 1 - pow(1 - t, power);
        float2 P1B = float2(pa, pa);
        float2 P1A = float2(1, 1);
        float2 P2A = P1A;
        float2 P2B = float2(1 + PreventClamping.y, 1);
        float2 P = lerp(lerp(P1B, P1A, t), lerp(P1A, P2B, t), t);
        float xx = P.y;

        float3 fixedHsb = rgb2hsb(float3(xx, xx, xx));
        hsb.z = fixedHsb.z;
    }

    hsb.z = SCurve(saturate(hsb.z * 2), Contrast + 1, 1) / 2;
    hsb.z = Brightness > 0
                ? lerp(hsb.z, 1, Brightness)
                : lerp(0, hsb.z, Brightness + 1);

    hsb.z = clamp(hsb.z, 0, 1);
    c.rgb = hsb2rgb(hsb);

    if (Colorize.a > 0)
    {
        float t = (c.r + c.g + c.b) / 3 + 0.0001;
        float3 colorizeHsb = rgb2hsb(Colorize.rgb);
        float complementary = mod(colorizeHsb.x + 0.5, 1);

        float3 darks = hsb2rgb(float3(complementary, colorizeHsb.y, OrangeTeal)).rgb;

        // Prevent darkening brights
        if (t < 0.5)
        {
            float3 mapped = lerp(darks, Colorize.rgb, t * 2);
            c.rgb = lerp(c.rgb, mapped, Colorize.a);
        }
        else
        {
            float3 mapped = lerp(Colorize.rgb, float3(1, 1, 1), t * 2 - 1);
            c.rgb = lerp(c.rgb, mapped, Colorize.a);
        }
    }

    // c.rgb = clamp(c.rgb, 0.000001,1000);
    // c.a = clamp(a,0,1);
    // return c;

    // float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
    float a2 = clamp(Background.a + c.a - Background.a * c.a, 0, 1);
    float3 rgb2 = clamp((1.0 - c.a) * Background.rgb + c.a * c.rgb, 0.00001, 100);
    return float4(rgb2, a2);
}
