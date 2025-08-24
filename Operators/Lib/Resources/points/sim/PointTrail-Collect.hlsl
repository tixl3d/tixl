#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params2 : register(b0)
{
    int CycleIndex;
    int TrailLength;
    int PointCount;
    int BufferLength;
    int WriteOrderTo;
    int WriteLineSeperators;
}

StructuredBuffer<Point> SourcePoints : t0;  // input
RWStructuredBuffer<Point> TrailPoints : u0; // output

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    int sourceIndex = i.x;
    if (i.x >= PointCount)
        return;

    int targetIndex = (CycleIndex + sourceIndex * TrailLength) % BufferLength;
    TrailPoints[targetIndex] = SourcePoints[sourceIndex];
}
