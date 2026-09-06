#include "shared/point.hlsl"

// Per-segment arc-length pre-pass for DrawTubes physical UVs.
// Writes, for each point i in [0, N-2], the arc length of segment i (in fixed-point
// millimeters) to OutSegMM[i]; OutSegMM[N-1] is left 0. An exclusive prefix sum of this
// buffer then yields the cumulative arc length at every point, which the vertex shader
// uses for a constant-density "along the tube" U coordinate.

cbuffer ArcParams : register(b0)
{
    float UseScale;
    float UniformScale;
    float Smooth;
};

StructuredBuffer<Point> Points : register(t0);
RWStructuredBuffer<uint> OutSegMM : register(u0);

// Mirrors DrawTubes.hlsl so the measured arc length matches the drawn centerline.
float3 LineStretch(int i, uint pointCount)
{
    if (pointCount < 2 || UseScale < 0.5 || UniformScale >= 0.5) return float3(0, 0, 0);

    float3 pos = Points[i].Position;
    float3 disp = 0;

    if (i > 0)
    {
        float s = Points[i - 1].Scale.x;
        s = isnan(s) ? 1 : s;
        disp += (s - 1.0) * 0.5 * (pos - Points[i - 1].Position);
    }

    if (i < (int)pointCount - 1)
    {
        float s = Points[i + 1].Scale.x;
        s = isnan(s) ? 1 : s;
        disp += (s - 1.0) * 0.5 * (pos - Points[i + 1].Position);
    }

    return disp;
}

float3 EffectivePos(int i, uint pointCount)
{
    return Points[i].Position + LineStretch(i, pointCount);
}

float3 getTangent(int i, uint pointCount)
{
    if (pointCount < 2) return float3(1, 0, 0);
    if (i == 0)          return EffectivePos(1, pointCount) - EffectivePos(0, pointCount);
    if (i == (int)pointCount - 1) return EffectivePos(i, pointCount) - EffectivePos(i - 1, pointCount);
    return (EffectivePos(i + 1, pointCount) - EffectivePos(i - 1, pointCount)) * 0.5;
}

float3 hermitePos(float3 p0, float3 m0, float3 p1, float3 m1, float t)
{
    float t2 = t * t, t3 = t2 * t;
    return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
}

// Arc length of one segment in world units, matching the vertex shader's geometry.
float SegmentArcLength(int i, uint pointCount)
{
    if (IsSeparator(Points[i]) || IsSeparator(Points[i + 1])) return 0.0;

    float3 pos0 = EffectivePos(i, pointCount);
    float3 pos1 = EffectivePos(i + 1, pointCount);

    if (Smooth >= 0.5)
    {
        // Approximate the Hermite curve length with a fixed chord subdivision.
        float3 m0 = getTangent(i, pointCount);
        float3 m1 = getTangent(i + 1, pointCount);
        const int Sub = 8;
        float len = 0.0;
        float3 prev = pos0;
        for (int s = 1; s <= Sub; s++)
        {
            float t = (float)s / (float)Sub;
            float3 cur = hermitePos(pos0, m0, pos1, m1, t);
            len += length(cur - prev);
            prev = cur;
        }
        return len;
    }

    return length(pos1 - pos0);
}

[numthreads(256, 1, 1)]
void csMain(uint3 id : SV_DispatchThreadID)
{
    uint pointCount, ptStride;
    Points.GetDimensions(pointCount, ptStride);

    uint i = id.x;
    if (i >= pointCount) return;

    // One entry per point; the last element stays 0 so an exclusive scan gives the
    // cumulative arc length at every point (s[N-1] == total length).
    if (i >= pointCount - 1)
    {
        OutSegMM[i] = 0;
        return;
    }

    float lenMm = SegmentArcLength((int)i, pointCount) * 1000.0;
    if (isnan(lenMm) || isinf(lenMm)) lenMm = 0.0;
    OutSegMM[i] = (uint)min(round(lenMm), 4294967295.0);
}
