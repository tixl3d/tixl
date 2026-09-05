#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

RWStructuredBuffer<Particle> Particles : register(u0);
RWStructuredBuffer<Point> ResultPoints : register(u1);

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint newPointCount, pointStride;
    Particles.GetDimensions(newPointCount, pointStride);

    uint gi = i.x;
    if(gi >= newPointCount)
        return;

    ResultPoints[gi] = Particles[gi];
}
