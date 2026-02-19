#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 Direction;
    float Amount;

    float RandomAmount;
    float SpeedFactor;
}

RWStructuredBuffer<Particle> Particles : u0;

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);

    if (i.x >= maxParticleCount)
        return;

    float3 offset = Direction * Amount * (1 + hash11(i.x) * RandomAmount) * SpeedFactor;
    Particles[i.x].Velocity += offset;
}
