#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float AddSeparatorThreshold;
}

cbuffer Params2 : register(b1)
{
    int CycleIndex;
    int TrailLength;
    int PointCount;
    int WriteOrderTo;
}

StructuredBuffer<Point> SourcePoints : t0;  // input
RWStructuredBuffer<Point> TrailPoints : u0; // output

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    int sourceIndex = i.x;
    if (i.x >= PointCount)
        return;

    // uint trailLength = (uint)(TrailLength + 0.5);
    int bufferLength = PointCount * TrailLength;
    int targetIndex = (CycleIndex + sourceIndex * TrailLength) % bufferLength;

    TrailPoints[targetIndex] = SourcePoints[sourceIndex];

    if (AddSeparatorThreshold > 0)
    {
        float3 lastPos = TrailPoints[(targetIndex - 1 + bufferLength) % bufferLength].Position;
        float3 pos = SourcePoints[sourceIndex].Position;
        if (length(lastPos - pos) > AddSeparatorThreshold)
            TrailPoints[targetIndex].Scale = NAN;
    }

    // Flag follow position W as NaN line seperator
    TrailPoints[(targetIndex + 1) % bufferLength].Scale = NAN;
}
