#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
cbuffer Params : register(b0)
{
    float CountA;
    float CountB;
}

// struct LegacyPoint {
//     float3 Position;
//     float W;
// };

StructuredBuffer<LegacyPoint> Points1 : t0;         // input
StructuredBuffer<LegacyPoint> Points2 : t1;         // input
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;    // output

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint countA = (uint)(CountA+ 1.5);
    bool useFirst = (i.x < countA);

    if(i.x > CountA + CountB) {
        ResultPoints[i.x].W = NAN; // NaN
        return;
    }

    if(useFirst) {
        ResultPoints[i.x] = Points1[i.x];
        if(i.x == countA-1) {
            ResultPoints[i.x].W = NAN;
        }
    }
    else {
        ResultPoints[i.x] = Points2[i.x - countA];
        if(i.x == countA + uint(CountB + 0.5)) {
            ResultPoints[i.x].W = NAN;
        }
    }
}
