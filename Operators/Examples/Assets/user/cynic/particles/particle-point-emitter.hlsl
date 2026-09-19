#include "shared/particle.hlsl"

cbuffer TimeConstants : register(b0)
{
    float globalTime;
    float time;
    float runTime;
    float dummy;
}

cbuffer CountConstants : register(b1)
{
    int4 bufferCount;
}

RWStructuredBuffer<Particle> Particles : u0;
ConsumeStructuredBuffer<ParticleIndex> DeadParticles : u1;

uint wang_hash(in out uint seed)
{
    seed = (seed ^ 61) ^ (seed >> 16);
    seed *= 9;
    seed = seed ^ (seed >> 4);
    seed *= 0x27d4eb2d;
    seed = seed ^ (seed >> 15);
    return seed;
}

[numthreads(1,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= bufferCount.x)
        return; // no particles available

    float radius = 5.0;
    float speed = 1.1;
    float3 emitPosition = float3(sin(runTime*speed)*radius, fmod(runTime*0.5, 30.0) - 15.0, cos(runTime*speed)*radius);
    float3 direction = normalize(float3(cos(runTime*speed)*radius, 0, -sin(runTime*speed)*radius));
    ParticleIndex pi = DeadParticles.Consume();
        
    Particle particle = Particles[pi.index];
    uint rng_state = uint(runTime*1000.0) + i.x;

    // float f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    // float f1 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    // float f2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    particle.position = emitPosition;//float3(f0*200.0,f1*200.0,f2*200.0);

    float f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    particle.lifetime = f0 * 5.0 + 15.0;

    f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    float f1 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    float f2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    particle.velocity = -direction + direction*float3(f0, f1, f2);
    // particle.velocity = float3(0,0,0);

    f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    f1 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    f2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    particle.color = float4(0.1,0.1,0.1,0.1);//float4(f0,f1,f2,1.0);

    Particles[pi.index] = particle;
}

