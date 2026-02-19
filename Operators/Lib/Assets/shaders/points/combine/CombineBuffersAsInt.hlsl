#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
cbuffer Params : register(b0)
{
    int StartIndex;
    int Length;
}

StructuredBuffer<Point> Points : t0;
RWStructuredBuffer<Point> ResultPoints : u0;

[numthreads(256, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    // if (i.x >= Length)
    //     return;

    ResultPoints[StartIndex + i.x] = Points[i.x];
}
