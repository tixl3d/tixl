cbuffer ParamConstants : register(b0)
{
    float StepCount;
    float Bias;
    float Offset;
    float HighlightIndex;

    float4 Highlight;
    float SmoothRadius;
    float Repeat;
    float UseSuperSampling;
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

Texture2D<float4> ImageA : register(t0);
Texture2D<float4> RampImageA : register(t1);
sampler texSampler : register(s0);

float mod(float x, float y)
{
    return ((x) - (y)*floor((x) / (y)));
}

int modi(int x, int y)
{
    return x >= 0 ? (x % y)
                  : y - (-x % -y);
}

float Bias2(float x, float bias)
{
    return x / ((1 / bias - 2) * (1 - x) + 1);
}

static float spreadFactor = (1 + 1 / (StepCount - 1)); // Saturate modulo to complete gradient width

float4 ComputeColor(float2 uv)
{
    float4 orgColor = ImageA.Sample(texSampler, uv);

    float gray = saturate((orgColor.r + orgColor.g + orgColor.b) / 3);
    float biased = Bias2(gray, Bias);
    float c = biased + Offset / StepCount;
    float modulo = Repeat > 0.5 ? mod(c, 1)
                                : saturate(c);

    int index = (int)(modulo * StepCount);

    float main = index / (StepCount - 1);
    float remainder = (modulo - main) * StepCount + main;

    // Attempt to have a smooth transition at outer colors
    if (Repeat > 0.5)
    {
        float modulo2 = Repeat > 0.5 ? mod(c - 0.01 + 1 / StepCount, 1)
                                     : saturate(c);
        int index2 = (int)(modulo2 * StepCount);
        float main2 = index2 / (StepCount - 1);
        float r = 100;
        float edge = saturate(abs(gray - 0.5) * 2 * r - r + 1);
        float offsetMod = mod(Offset - 0.01, 1);
        float newEdge = lerp(main, main2, offsetMod);
        main = lerp(main, newEdge, edge);
    }

    bool isHighlight = index == modi((int)HighlightIndex, (int)StepCount);
    float4 rampColor = RampImageA.Sample(texSampler, float2(main, 0.5 / 2));

    // float4 mainRampColor = isHighlight ? Highlight : rampColor;
    float4 mainRampColor = isHighlight
                               ? lerp(rampColor, Highlight, Highlight.a)
                               : rampColor;

    // A legacy implementation that was using ramp color for alpha of highlight areas
    // This turned out to be limiting for highlights on transparent ramps.

    // float4 mainRampColor = isHighlight ? float4(lerp(rampColor.rgb, Highlight.rgb, Highlight.a), rampColor.a)
    //                                             : rampColor;
    float4 edgeRampColor = RampImageA.Sample(texSampler, float2(remainder, 1.5 / 2));

    float a = clamp(mainRampColor.a + edgeRampColor.a - mainRampColor.a * edgeRampColor.a, 0, 1);
    float3 rgb = (1.0 - edgeRampColor.a) * mainRampColor.rgb + edgeRampColor.a * edgeRampColor.rgb;
    return float4(rgb, a);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 p = psInput.texCoord;

    if (UseSuperSampling > 0.5)
    {
        float4 offsets[2];
        offsets[0] = float4(-0.375, 0.125, 0.125, 0.375);
        offsets[1] = float4(0.375, -0.125, -0.125, -0.375);

        float2 sxy = float2(TargetWidth, TargetHeight);

        return (ComputeColor(p + offsets[0].xy / sxy) +
                ComputeColor(p + offsets[0].zw / sxy) +
                ComputeColor(p + offsets[1].xy / sxy) +
                ComputeColor(p + offsets[1].zw / sxy)) /
               4;
    }
    else
    {
        return ComputeColor(p);
    }
}