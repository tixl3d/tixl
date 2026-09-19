#include "shared/particle.hlsl"

cbuffer CountConstants : register(b0)
{
    int4 bufferCount;
}

cbuffer RemoveConstants : register(b1)
{
    float idToRemove;
}

RWStructuredBuffer<Particle> Particles : u0;
RWStructuredBuffer<ParticleIndex> AliveParticles : u1;
AppendStructuredBuffer<ParticleIndex> DeadParticles : u2;

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= bufferCount.x)
        return; // only check alive particles

    ParticleIndex pi = AliveParticles[i.x];
    Particle particle = Particles[pi.index];
    if (particle.lifetime >= 0.0 && particle.emitterId == (int)(idToRemove + 0.5))
    {
        particle.lifetime = -1.0;
        particle.emitterId = -1;
        DeadParticles.Append(pi);
        Particles[pi.index] = particle;
    }
}

