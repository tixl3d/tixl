#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
cbuffer Params : register(b0)
{
    float CountA;
    float CountB;
}

// struct Point {
//     float3 Position;
//     float W;
// };

StructuredBuffer<Point> Points1 : register(t0);         // input
StructuredBuffer<Point> Points2 : register(t1);         // input
RWStructuredBuffer<Point> ResultPoints : register(u0);    // output

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint countA = (uint)(CountA+ 1.5);
    bool useFirst = (i.x < countA);

    if(i.x > CountA + CountB) {
        ResultPoints[i.x].Scale = NAN;
        return;
    }

    if(useFirst) {
        ResultPoints[i.x] = Points1[i.x];
        if(i.x == countA-1) {
            ResultPoints[i.x].Scale = NAN;
        }
    }
    else {
        ResultPoints[i.x] = Points2[i.x - countA];
        if(i.x == countA + uint(CountB + 0.5)) {
            ResultPoints[i.x].Scale = NAN;
        }
    }
}
