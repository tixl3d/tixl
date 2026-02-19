#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float StartIndex;
    float Scatter;
    float Seed;
    float StepSize;
}

StructuredBuffer<Point> SourcePoints : t0;
RWStructuredBuffer<Point> ResultPoints : u0;

int imod(int x, int y)
{
    return x >= 0 ? x % y
                  : y + ((x + 1) % y) - 1; // there are probably easier ways to do this
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint sourceCount, stride;
    SourcePoints.GetDimensions(sourceCount, stride);

    uint resultCount, stride2;
    ResultPoints.GetDimensions(resultCount, stride2);

    if (i.x >= resultCount)
    {
        return;
    }

    uint scatterOffset = Scatter > 0.001
                             ? (float)sourceCount * Scatter * hash11(i.x + Seed % 4321 + int(StartIndex + 0.5) % 1234)
                             : 0;

    // uint index = imod((int)StartIndex + (i.x * StepSize) + scatterOffset + 0.1,  sourceCount);
    int index = imod(int(StartIndex + 0.5) + (i.x * StepSize) + scatterOffset, sourceCount);
    ResultPoints[i.x] = SourcePoints[index];
}
