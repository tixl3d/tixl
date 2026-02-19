#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    int CycleIndex;
    int TrailLength;
    int PointCount;
    int BufferLength;
    int WriteOrderTo;
    int WriteLineSeparators;
}

StructuredBuffer<Point> CyclePoints : t0;   // input
RWStructuredBuffer<Point> TrailPoints : u0; // output

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= BufferLength)
        return;

    int sourceIndex = (i.x + CycleIndex) % BufferLength;
    int targetIndex = BufferLength - i.x - 1;

    TrailPoints[targetIndex] = CyclePoints[sourceIndex];

    float fInBuffer = 1;

    int indexInTrail = targetIndex % TrailLength;
    if (WriteLineSeparators)
    {
        fInBuffer = TrailLength > 1 ? (1 - indexInTrail / (float)(TrailLength - 1))
                                    : 0.5;

        if (indexInTrail == TrailLength - 1)
        {
            TrailPoints[targetIndex].Scale = NAN;
        }
    }
    else
    {
        fInBuffer = TrailLength > 2 ? (1 - (indexInTrail) / (float)(TrailLength - 1))
                                    : 0.5;
    }

    if (WriteOrderTo == 1)
    {
        TrailPoints[targetIndex].FX1 = fInBuffer;
    }
    else if (WriteOrderTo == 2)
    {
        TrailPoints[targetIndex].FX2 = fInBuffer;
    }
    else if (WriteOrderTo == 3)
    {
        TrailPoints[targetIndex].Scale = fInBuffer;
    }
}