#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float CountA;
    float CountB;
    float ResultCount;
    float InitWTo01;
}

StructuredBuffer<Point> PointsA : t0;        // input
StructuredBuffer<Point> PointsB : t1;        // input
RWStructuredBuffer<Point> ResultPoints : u0; // output

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint totalCount, stride;
    ResultPoints.GetDimensions(totalCount, stride);

    if (i.x > (uint)ResultCount * 3)
        return;

    uint pairIndex = i.x / 3;
    uint pairElement = i.x % 3;

    if (pairElement == 1)
    {
        ResultPoints[i.x] = PointsB[pairIndex % (uint)CountB];
        if (InitWTo01 > 0.5)
            ResultPoints[i.x].FX1 = 1;
    }
    else
    {
        ResultPoints[i.x] = PointsA[pairIndex % (uint)CountA];
        if (InitWTo01 > 0.5)
            ResultPoints[i.x].FX1 = 0;
    }

    // Add Seperator
    if (pairElement == 2)
        ResultPoints[i.x].Scale = NAN; // NaN for divider
}
