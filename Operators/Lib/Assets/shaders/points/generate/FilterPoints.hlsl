#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer FloatParams : register(b0)
{
    float Scatter;
    float StepSize;
}

cbuffer IntParams : register(b1)
{
    int SourceCount;
    int ResultCount;
    int StartIndex;
    int Seed;
}

StructuredBuffer<Point> SourcePoints : register(t0);
RWStructuredBuffer<Point> ResultPoints : register(u0);

inline int imod2(int val, int repeat) 
{
    int x = val % repeat;
    return x + (x < 0) * repeat;
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    int idx= i.x;

    if (idx >= ResultCount)
        return;

    int scatterOffset = Scatter > 0.001
                             ? (float)SourceCount * Scatter * hash11u(idx + Seed * SourceCount + StartIndex)
                             : 0 ;

    int index = imod2(StartIndex + (int)((i.x * StepSize) + scatterOffset), SourceCount);
    ResultPoints[i.x] = SourcePoints[index];
}
