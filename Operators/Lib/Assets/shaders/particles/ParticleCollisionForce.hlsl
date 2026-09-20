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

    // A Particle and a Point share a layout but not their member names, and this used to be one
    // assignment that reinterpreted the fields. Spelled out, it copies exactly what it copied before -
    // including Velocity landing in Scale, which is worth a second look.
    ResultPoints[gi].Position = Particles[gi].Position;
    ResultPoints[gi].FX1 = Particles[gi].Radius;
    ResultPoints[gi].Rotation = Particles[gi].Rotation;
    ResultPoints[gi].Color = Particles[gi].Color;
    ResultPoints[gi].Scale = Particles[gi].Velocity;
    ResultPoints[gi].FX2 = Particles[gi].BirthTime;
}
