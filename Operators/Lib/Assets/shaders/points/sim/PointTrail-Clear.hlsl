#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    // float TrailLength;
    // float CycleIndex;
    // float HasPointCountChanged;
}

RWStructuredBuffer<Point> TrailPoints : register(u0); // output

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    TrailPoints.GetDimensions(pointCount, stride);

    TrailPoints[i.x].Scale = NAN;
}
