// Reads one frame of a frame-major track buffer (index = frame * MarkerCount + marker) and interpolates
// between the two frames around a fractional frame index. A marker lost in either frame stays lost.

#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float Frame;
    int MarkerCount;
    int FrameCount;
    int __padding;
}

StructuredBuffer<Point> TrackPoints : register(t0);
RWStructuredBuffer<Point> ResultPoints : register(u0);

[numthreads(64, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint marker = id.x;
    if (marker >= (uint)MarkerCount)
        return;

    float clampedFrame = clamp(Frame, 0, FrameCount - 1);
    int frameA = (int)floor(clampedFrame);
    int frameB = min(frameA + 1, FrameCount - 1);
    float blend = clampedFrame - frameA;

    Point a = TrackPoints[frameA * MarkerCount + marker];
    Point b = TrackPoints[frameB * MarkerCount + marker];

    bool outsideTrack = Frame < 0 || Frame > FrameCount - 1;
    if (outsideTrack || IsSeparator(a) || (blend > 0 && IsSeparator(b)))
    {
        Point lost = a;
        lost.Scale = NAN;
        lost.FX2 = marker;
        ResultPoints[marker] = lost;
        return;
    }

    if (blend <= 0)
    {
        ResultPoints[marker] = a;
        return;
    }

    Point p;
    p.Position = lerp(a.Position, b.Position, blend);
    p.FX1 = lerp(a.FX1, b.FX1, blend);
    p.Rotation = normalize(lerp(a.Rotation, b.Rotation * (dot(a.Rotation, b.Rotation) < 0 ? -1 : 1), blend));
    p.Color = lerp(a.Color, b.Color, blend);
    p.Scale = lerp(a.Scale, b.Scale, blend);
    p.FX2 = a.FX2;
    ResultPoints[marker] = p;
}
