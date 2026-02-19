/*

This shader is not intended to perform a physically accurate simulation. Instead,
it employs a stylistic approach to control the visual appearance of points using a
set of control points. For computing alignment and position, we calculate a gravity
vector, which is then used for orientation and offset.

Blending point attributes using gravity can be challenging because the gravitational
effect increases dramatically at close distances and diminishes quickly over longer
distances. To manage this, we use smoothed linear distance weights instead.

The meaningful blending range for distances is controlled by the Length and Phase parameters.
*/

#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer Params : register(b0)
{
    float Amount;
    float Range;
    float OffsetRange;
    float AffectPosition;

    float3 OrientationUpVector;
    float AffectOrientation;

    float AffectW;
    float AffectColor;
    float2 GainAndBias;
    float Variation;
}

cbuffer Params : register(b1)
{
    int FieldCount;
    int ColorMode;
    int WMode;
    int WCurveAffectsWeight;
}

#define COLORMODE_REPLACE_ADD 0
#define COLORMODE_REPLACE_AVERAGE 1
#define COLORMODE_BLEND 2

#define WMODE_SET 0
#define WMODE_ADD 1
#define WMODE_BLEND 2

StructuredBuffer<LegacyPoint> SourcePoints : t0;
StructuredBuffer<LegacyPoint> FieldPoints : t1;

Texture2D<float4> CurveImage : register(t2);
Texture2D<float4> GradientImage : register(t3);

RWStructuredBuffer<LegacyPoint> ResultPoints : u0;

sampler texSampler : register(s0);

float3 fmod(float3 x, float3 y)
{
    return (x - y * floor(x / y));
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x;
    uint pointCount, stride;
    SourcePoints.GetDimensions(pointCount, stride);
    if (index >= pointCount)
    {
        return;
    }

    LegacyPoint p = SourcePoints[index];

    float3 totalForce;
    float totalWeight = 0;

    float4 totalColor = float4(0, 0, 0, 0);
    float totalW;
    int usedCount = 0;

    float noise = (hash11u(index) - 0.5) * Variation;

    for (int fieldIndex = 0; fieldIndex < FieldCount; fieldIndex++)
    {
        float w = FieldPoints[fieldIndex].W;
        if (isnan(w) || w < 0.0001)
            continue;

        usedCount++;
        float3 fPos = FieldPoints[fieldIndex].Position;
        float3 dir = (p.Position - fPos) / w;
        float len = length(dir);
        float dd = 1 / (len + 0.1);

        float f = (1 - saturate((len - OffsetRange) / Range)) + noise;
        f = ApplyGainAndBias(f, GainAndBias);
        f *= p.Selected;

        float fw = CurveImage.SampleLevel(texSampler, float2(f, 0.5), 0).r;
        totalW += fw;

        f *= WCurveAffectsWeight ? fw : 1;

        float4 color = GradientImage.SampleLevel(texSampler, float2(f, 0.5), 0);
        totalColor += FieldPoints[fieldIndex].Color * color * (ColorMode == COLORMODE_BLEND ? f : 1);

        // weight = weightFactor;
        totalWeight += f;

        float distanceSq = dot(dir, dir);
        if (distanceSq > 0.0001)
        {
            // Compute the gravitational force vector (without mass, as it's constant)
            float invDistance = rsqrt(distanceSq);
            float3 force = dir * invDistance * invDistance * invDistance; // r / |r|^3
            totalForce += force;
        }
    }

    float selectAmount = Amount * p.Selected;

    float gMagnitude = length(totalForce) + 0.0001;

    // Offset
    float3 dir = totalForce / gMagnitude;
    p.Position -= dir * totalWeight * selectAmount * AffectPosition;

    // Orient towards
    float4 lookAtRotation = normalize(qLookAt(-dir, OrientationUpVector));
    p.Rotation = qSlerp(p.Rotation, lookAtRotation, totalWeight * selectAmount * AffectOrientation);

    // Color
    float colorAffect = selectAmount * AffectColor;
    float4 c = 0;

    switch (ColorMode)
    {
    case COLORMODE_REPLACE_ADD:
        c = lerp(p.Color, totalColor, colorAffect);
        break;

    case COLORMODE_REPLACE_AVERAGE:
        if (totalWeight > 0.001)
        {
            totalColor /= totalWeight;
        }
        c = lerp(p.Color, totalColor, colorAffect);
        break;

    case COLORMODE_BLEND:
        if (totalWeight > 0.001)
        {
            totalColor /= totalWeight;
        }
        c = lerp(p.Color, totalColor, saturate(totalWeight) * colorAffect);
        break;
    }

    p.Color = float4(max(c.rgb, 0), saturate(c.a));

    // W
    float wAffect = selectAmount * AffectW;
    switch (WMode)
    {
    case WMODE_SET:
        p.W = totalW * wAffect;
        break;
    case WMODE_ADD:
        p.W += totalW * wAffect;
        break;
    case WMODE_BLEND:
        p.W = lerp(p.W, totalW, totalWeight * wAffect);
        break;
    }

    ResultPoints[index] = p;
}
