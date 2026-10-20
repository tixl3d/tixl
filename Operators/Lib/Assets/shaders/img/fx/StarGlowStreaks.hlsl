#include "shared/blend-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 Color;
    float Range;
    float Brightness;
    float Threshold; // Color key threshold (0-1)
    float BlendMode;

    float4 OriginalColor;
    float Quality; //Attemps to use Mips in order to make the FX less expensive
    float GlareModes;
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

// Distance between two taps (in uv units)
static const float Steps = 0.002;

static const float ThresholdSoftness = 0.25;

float4 SampleStreak(float2 uv, float2 offset)
{
    float4 s = Image.SampleLevel(texSampler, uv + offset, Quality);
    float sum = s.r + s.g + s.b;
    float t = Threshold * 3.0;
    return s * smoothstep(t, t + ThresholdSoftness, sum);
}

float4 psMain(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;

    float width, height;
    Image.GetDimensions(width, height);

    int mode = (int)round(GlareModes);
    bool diagonals = mode == 0 || mode == 2;
    bool horizontal = mode == 1 || mode == 2 || mode == 3;
    bool vertical = mode == 1 || mode == 2 || mode == 4;

    float range = max(Range, Steps);
    float2 asp = height / float2(width, height);

    // Taps sit on a fixed grid centered on the pixel (k * Steps), so changing
    // Range only adds or removes taps at the far end, where falloff is ~0.
    int count = (int)(range / Steps);

    float4 streaksColor = float4(0.0, 0.0, 0.0, 1.0);

    for (int k = -count; k <= count; k++)
    {
        float i = k * Steps;
        float falloff = 1.0 - abs(i) / range;
        float weight = falloff * Steps * Brightness;

        if (diagonals)
        {
            streaksColor += SampleStreak(uv, i * asp) * weight;
            streaksColor += SampleStreak(uv, float2(i, -i) * asp) * weight;
        }
        if (horizontal)
        {
            streaksColor += SampleStreak(uv, float2(i * 1.5, 0) * asp) * weight;
        }
        if (vertical)
        {
            streaksColor += SampleStreak(uv, float2(0, i * 1.5) * asp) * weight;
        }
    }

    float4 orgColor = Image.Sample(texSampler, uv) * OriginalColor;

    return BlendColors(orgColor, streaksColor * Color, (int)BlendMode);
}
