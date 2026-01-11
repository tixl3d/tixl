// Single-pass 2D blur with switchable kernels via define.
//
// Set FASTBLUR_KERNEL at compile time:
//   1 = 5-tap (diagonal quincunx)
//   2 = 9-tap tent (3x3, good default)
//   3 = 9-tap bokeh-ish (center + 8 ring)
//   4 = 13-tap bokeh-ish (center + 6 inner ring + 6 outer ring)

#ifndef FASTBLUR_KERNEL
#define FASTBLUR_KERNEL 1
#endif

cbuffer Blur2DParams : register(b0)
{
    float RadiusPx; // radius in pixels in THIS level’s pixel space
    float Width;
    float Height;
    float Clamp01; // 0 or 1
};

Texture2D InputTexture : register(t0);
SamplerState LinearSampler : register(s0);

struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

static const float PI = 3.14159265359;

float4 Sample9Tent(float2 uv, float2 d)
{
    // 3x3 tent weights: center 4, cardinals 2, diagonals 1 => sum 16
    float4 c = 0;
    c += InputTexture.Sample(LinearSampler, uv) * (4.0 / 16.0);

    c += InputTexture.Sample(LinearSampler, uv + float2(d.x, 0)) * (2.0 / 16.0);
    c += InputTexture.Sample(LinearSampler, uv + float2(-d.x, 0)) * (2.0 / 16.0);
    c += InputTexture.Sample(LinearSampler, uv + float2(0, d.y)) * (2.0 / 16.0);
    c += InputTexture.Sample(LinearSampler, uv + float2(0, -d.y)) * (2.0 / 16.0);

    c += InputTexture.Sample(LinearSampler, uv + float2(d.x, d.y)) * (1.0 / 16.0);
    c += InputTexture.Sample(LinearSampler, uv + float2(-d.x, d.y)) * (1.0 / 16.0);
    c += InputTexture.Sample(LinearSampler, uv + float2(d.x, -d.y)) * (1.0 / 16.0);
    c += InputTexture.Sample(LinearSampler, uv + float2(-d.x, -d.y)) * (1.0 / 16.0);

    return c;
}

float4 Sample5Quincunx(float2 uv, float2 d)
{
    // Center + 4 diagonals (less axis-y than a plus-shaped 5 tap)
    float4 c = 0;
    c += InputTexture.Sample(LinearSampler, uv) * 0.50;
    c += InputTexture.Sample(LinearSampler, uv + float2(d.x, d.y)) * 0.125;
    c += InputTexture.Sample(LinearSampler, uv + float2(-d.x, d.y)) * 0.125;
    c += InputTexture.Sample(LinearSampler, uv + float2(d.x, -d.y)) * 0.125;
    c += InputTexture.Sample(LinearSampler, uv + float2(-d.x, -d.y)) * 0.125;
    return c;
}

float4 SampleBokeh9(float2 uv, float2 d)
{
    // Center + 8 around a circle (octagon). “Bokeh-ish” because energy lives on a ring.
    // Sum weights = 1.0
    float4 c = 0;
    c += InputTexture.Sample(LinearSampler, uv) * 0.12;

    [unroll] for (int i = 0; i < 8; i++)
    {
        float a = (2.0 * PI) * (float)i / 8.0;
        float2 dir = float2(cos(a), sin(a));
        c += InputTexture.Sample(LinearSampler, uv + dir * d) * 0.11;
    }

    return c;
}

float4 SampleBokeh13(float2 uv, float2 d)
{
    // Center + 6 inner ring + 6 outer ring (hex aperture feel).
    // Inner ring uses radius 0.75, outer ring uses radius 1.5
    // Sum weights ~ 1.0
    float4 c = 0;
    c += InputTexture.Sample(LinearSampler, uv) * 0.08;

    float2 dInner = d * 0.75;
    float2 dOuter = d * 1.50;

    [unroll] for (int i = 0; i < 6; i++)
    {
        float a = (2.0 * PI) * (float)i / 6.0;
        float2 dir = float2(cos(a), sin(a));
        c += InputTexture.Sample(LinearSampler, uv + dir * dInner) * 0.06;
        c += InputTexture.Sample(LinearSampler, uv + dir * dOuter) * 0.0933333333;
    }

    return c;
}

float4 psMain(PS_INPUT input) : SV_Target
{
    float2 texel = float2(1.0 / Width, 1.0 / Height);
    float2 d = texel * RadiusPx;

#if FASTBLUR_KERNEL == 1
    float4 c = Sample5Quincunx(input.uv, d);
#elif FASTBLUR_KERNEL == 2
    float4 c = Sample9Tent(input.uv, d);
#elif FASTBLUR_KERNEL == 3
    float4 c = SampleBokeh9(input.uv, d);
#elif FASTBLUR_KERNEL == 4
    float4 c = SampleBokeh13(input.uv, d);
#else
    float4 c = Sample9Tent(input.uv, d);
#endif

    if (Clamp01 > 0.5)
        c = saturate(c);

    return c;
}