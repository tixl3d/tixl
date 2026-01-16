// RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 LineColor;
    float4 BackgroundColor;

    float2 RangeX;
    float2 RangeY;

    float MajorLineWidth; // 1.6
    float MinorLineWidth; // 1.0

    float2 Density;
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

#define mod(x, y) (x - y * floor(x / y))

static const float INV_LN10 = 0.4342944819f; // 1/ln(10)
float log10f(float x) { return log(max(x, 1e-20f)) * INV_LN10; }

float Grid1D(float p, float step, float thicknessPx)
{
    float s = p / step;
    float fs = fwidth(s); // "cells" per pixel (AA width in scaled space)

    float d = min(frac(s), 1.0 - frac(s)); // distance to nearest grid line in [0..0.5]
    float w = thicknessPx * fs;            // line half-width in same scaled space

    // 1 at line, 0 away from line (smooth AA)
    return 1.0 - smoothstep(w, w + fs, d);
}

float Grid2D(float2 p, float2 step, float thicknessPx)
{
    return max(Grid1D(p.x, step.x, thicknessPx), Grid1D(p.y, step.y, thicknessPx));
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    // uv.y = 1 - uv.y;
    float2 rasterUv = float2(uv.x, 1 - uv.y);
    float2 p = lerp(float2(RangeX.x, RangeY.x), float2(RangeX.y, RangeY.y), rasterUv);

    float2 unitsPerPixel = float2(fwidth(p.x), fwidth(p.y));
    unitsPerPixel = max(unitsPerPixel, 1e-20f);

    float2 rawStep = unitsPerPixel * Density;

    float decadeExpX = floor(log10f(rawStep.x));
    float decadeExpY = floor(log10f(rawStep.y));

    float majorStepX = pow(10.0, decadeExpX);
    float majorStepY = pow(10.0, decadeExpY);

    float minorStepX = majorStepX / 10.0;
    float minorStepY = majorStepY / 10.0;

    // [0..1) within the decade, per axis
    float fracDecX = log10f(rawStep.x / majorStepX);
    float fracDecY = log10f(rawStep.y / majorStepY);

    float minorFadeX = 1.0 - smoothstep(0.1, 0.9, fracDecX);
    float minorFadeY = 1.0 - smoothstep(0.1, 0.9, fracDecY);

    // Major masks per axis
    float majorX = Grid1D(p.x, majorStepX, MajorLineWidth);
    float majorY = Grid1D(p.y, majorStepY, MajorLineWidth);
    float majorMask = max(majorX, majorY);

    // Minor masks per axis, faded per axis
    float minorX = Grid1D(p.x, minorStepX, MinorLineWidth) * minorFadeX;
    float minorY = Grid1D(p.y, minorStepY, MinorLineWidth) * minorFadeY;
    float minorMask = max(minorX, minorY);

    float lineMask = max(majorMask, minorMask);
    // return lerp(BackgroundColor, LineColor, saturate(lineMask) * LineColor.a);
    float4 c = lerp(BackgroundColor, LineColor, saturate(lineMask) * LineColor.a);

    float distanceFromCenter = pow(saturate(abs(p.y * 0.1)), 0.3);

    float amplifyCenterLine = lerp(1.5, 1, saturate(abs(p.y * 100)));

    c *= lerp(1, saturate(lerp(float4(1, 0.6, 0.2, 1), float4(0.5, 0.7, 1, 1), saturate(p.y))), distanceFromCenter) * amplifyCenterLine;

    float4 orgColor = inputTexture.SampleLevel(texSampler, uv, 0.0);

    float a = orgColor.a * saturate(MixOriginal) + c.a - orgColor.a * saturate(MixOriginal) * c.a;
    float3 rgb = (1.0 - c.a) * clamp(orgColor.rgb, 0, 1) + c.a * c.rgb;
    return float4(clamp(rgb, 0, 10000), clamp(a, 0, 1));
}
