#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float SmoothDistance;
    float2 SampleRange;
    float __padding;
    float3 UpVector;
}

cbuffer Params : register(b1)
{
    int SourceCount;
    int ResultCount;
    int SampleMode;
    int SampleCount;
    int RotationMode;
}

StructuredBuffer<Point> SourcePoints : t0;   // input
RWStructuredBuffer<Point> ResultPoints : u0; // output

// static uint sourceCount;
static float3 sumPos = 0;
static float sumF1 = 0;
static float sumF2 = 0;
static float4 sumColor = 0;
static float3 sumScale = 0;
static int sampledCount = 0;

float3 SamplePosAtF(float f)
{
    float3 pos = 0;
    float sourceF = saturate(f) * (SourceCount - 1);
    uint index = (int)sourceF;
    if (index > SourceCount - 1)
        return pos;

    float w1 = SourcePoints[index].FX1;
    if (isnan(SourcePoints[index].Scale.x * SourcePoints[index + 1].Scale.x))
    {
        return pos;
    }

    float fraction = sourceF - index;
    sumF1 += lerp(SourcePoints[index].FX1, SourcePoints[index + 1].FX1, fraction);
    sumF2 += lerp(SourcePoints[index].FX2, SourcePoints[index + 1].FX2, fraction);
    pos = lerp(SourcePoints[index].Position, SourcePoints[index + 1].Position, fraction);
    sumPos += pos;
    sumColor += lerp(SourcePoints[index].Color, SourcePoints[index + 1].Color, fraction);
    sumScale += lerp(SourcePoints[index].Scale, SourcePoints[index + 1].Scale, fraction);

    sampledCount++;
    return pos;
}

inline float4 SampleRotationAtF(float f)
{
    float sourceF = saturate(f) * (SourceCount - 1);
    int index = (int)sourceF;
    float fraction = sourceF - index;
    index = clamp(index, 0, SourceCount - 1);
    return qSlerp(SourcePoints[index].Rotation, SourcePoints[index + 1].Rotation, fraction);
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    // uint PointCount, stride;
    // ResultPoints.GetDimensions(PointCount, stride);

    if (i.x >= ResultCount)
        return;

    float fNormlized = (float)i.x / (ResultCount);

    float rightFactor = SampleMode > 0.5 ? SampleRange.x : 0;
    float f = SampleRange.x + fNormlized * (SampleRange.y - rightFactor);

    if (f < 0 || f > 1)
    {
        ResultPoints[i.x].Scale = NAN;
        return;
    }

    sumF1 = 0;
    sampledCount = 0;
    SamplePosAtF(f);

    // int steps = clamp(SampleCount, 1, 10);
    float stepSize = SmoothDistance / (SampleCount * SourceCount);
    float d = stepSize;

    float3 minPos = SamplePosAtF(f - d);
    float3 maxPos = SamplePosAtF(f + d);

    for (int stepIndex = 1; stepIndex < SampleCount; stepIndex++)
    {
        d += stepSize;
        minPos += SamplePosAtF(f - d);
        maxPos += SamplePosAtF(f + d);
    }

    if (sampledCount == 0)
        sumF1 = NAN;

    float3 pos = sumPos / sampledCount;
    ResultPoints[i.x].Position = pos;

    ResultPoints[i.x].Color = sumColor / sampledCount;
    ResultPoints[i.x].Scale = sumScale / sampledCount;
    ResultPoints[i.x].FX1 = sumF1 / sampledCount;
    ResultPoints[i.x].FX2 = sumF2 / sampledCount;

    if (RotationMode == 1)
    {
        minPos /= stepSize;
        maxPos /= stepSize;

        float3 tangent = normalize(minPos - maxPos);
        ResultPoints[i.x].Rotation = qLookAt(tangent, UpVector);
    }
    else
    {
        ResultPoints[i.x].Rotation = SampleRotationAtF(f);
    }
}
