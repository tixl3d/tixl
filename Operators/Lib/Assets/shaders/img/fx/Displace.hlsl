// #include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float DisplaceAmount;
    float DisplaceOffset;
    float Twist;
    float Shade;

    float2 DisplaceMapOffset;
    float SampleRadius;
}

// cbuffer TimeConstants : register(b1)
// {
//     float globalTime;
//     float time;
//     float runTime;
//     float beatTime;
// }

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

cbuffer IntParameters : register(b2)
{
    int DisplaceMode;
    int UseRGSSMultiSampling;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
Texture2D<float4> DisplaceMap : register(t1);
sampler texSampler : register(s0);

float IsBetween(float value, float low, float high)
{
    return (value >= low && value <= high) ? 1 : 0;
}

static float displaceMapWidth;
static float displaceMapHeight;

float4 DoDisplace(float2 uv)
{
    float4 cx1, cx2, cy1, cy2;
    cx1 = cx2 = cy2 = cy1 = float4(0, 0, 0, 0);
    float radius2 = 2;
    float sx = SampleRadius / displaceMapWidth;
    float sy = SampleRadius / displaceMapHeight;
    // int sampleIndex = 1;

    // float padding = 1;
    // float paddingSum;
    float2 d = 0;
    float len = 0;

    float2 direction = 0;
    if (DisplaceMode < 1.5)
    {
        if (DisplaceMode < 0.5)
        {
            float4 cx1 = DisplaceMap.SampleLevel(texSampler, float2(uv.x + sx, uv.y) + DisplaceMapOffset, 0);
            float x1 = (cx1.r + cx1.g + cx1.b) / 3;
            float4 cx2 = DisplaceMap.SampleLevel(texSampler, float2(uv.x - sx, uv.y) + DisplaceMapOffset, 0);
            float x2 = (cx2.r + cx2.g + cx2.b) / 3;
            float4 cy1 = DisplaceMap.SampleLevel(texSampler, float2(uv.x, uv.y + sy) + DisplaceMapOffset, 0);
            float y1 = (cy1.r + cy1.g + cy1.b) / 3;
            float4 cy2 = DisplaceMap.SampleLevel(texSampler, float2(uv.x, uv.y - sy) + DisplaceMapOffset, 0);
            float y2 = (cy2.r + cy2.g + cy2.b) / 3;
            d += float2((x1 - x2), (y1 - y2));

            // paddingSum += padding;
            // padding /= 1.5;
        }
        else
        {
            float4 rgba = DisplaceMap.SampleLevel(texSampler, uv + DisplaceMapOffset, 0);
            d = float2(0.0, (rgba.r + rgba.g + rgba.b) / 3.0) / 10;
        }
        float a = (d.x == 0 && d.y == 0)
                      ? 0
                      : (atan2(d.x, d.y) + Twist / 180 * 3.14158);

        direction = float2(sin(a), cos(a));
        len = length(d) + 0.000001;
    }
    else
    {
        float4 rgba = DisplaceMap.SampleLevel(texSampler, uv + DisplaceMapOffset, 0);
        d = DisplaceMode < 0.5 ? (rgba.rg - 0.5) * 0.01
                               : rgba.rg * 0.01;
        len = length(d) + 0.000001;

        float rRad = Twist / 180 * 3.14158;
        float sina = sin(-rRad);
        float cosa = cos(-rRad);
        d = float2(
            cosa * d.x - sina * d.y,
            cosa * d.y + sina * d.x);

        direction = d / len;
    }

    float2 p2 = direction * (-DisplaceAmount * len * 10 + DisplaceOffset);
    float imgAspect = TargetWidth / TargetHeight;
    p2.x /= imgAspect;

    float4 c = Image.SampleLevel(texSampler, uv + p2, 0);

    c.rgb *= (1 - len * Shade * 100);
    return c;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    DisplaceMap.GetDimensions(displaceMapWidth, displaceMapHeight);

    float2 uv = psInput.texCoord;

    float4 c = 0;

    if (UseRGSSMultiSampling > 0.5)
    {
        // 4x rotated grid
        float4 offsets[2];
        offsets[0] = float4(-0.375, 0.125, 0.125, 0.375);
        offsets[1] = float4(0.375, -0.125, -0.125, -0.375);

        float2 sxy = float2(TargetWidth, TargetHeight);

        c = (DoDisplace(uv + offsets[0].xy / sxy) +
             DoDisplace(uv + offsets[0].zw / sxy) +
             DoDisplace(uv + offsets[1].xy / sxy) +
             DoDisplace(uv + offsets[1].zw / sxy)) /
            4;
    }
    else
    {
        c = DoDisplace(uv);
    }

    return clamp(c, 0, float4(999, 999, 999, 1));
}