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

cbuffer Parameter : register(b2)
{
    float4 color;
    float4 scatterColor;
    float4 randomVelocity;
    float lifetime;
    float ScatterPosition;
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


float getRandomFloat(in out uint rng_state)
{
    return float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
}

[numthreads(1,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= bufferCount.x)
        return; // no particles available

    ParticleIndex pi = DeadParticles.Consume();
        
    Particle particle = Particles[pi.index];
    uint rng_state = uint(runTime%1000*1000) + i.x * 12;

    float f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    float f1 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    float f2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    particle.position = float3(f0,f1,f2) * ScatterPosition;

    f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    particle.lifetime = lifetime;//f0 * 8.0 + 2.0;

    f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    f1 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    f2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    particle.velocity = float3(f0, f1, f2)*randomVelocity.xyz*randomVelocity.w*40.0;
    // particle.velocity = float3(0,0,0);

    f0 = getRandomFloat(rng_state) - 0.5;
    f1 = getRandomFloat(rng_state) - 0.5;
    f2 = getRandomFloat(rng_state) - 0.5;
    float f3 = saturate(getRandomFloat(rng_state) - 0.5);
    particle.color = color + scatterColor*float4(f0,f1,f2,f3);

    Particles[pi.index] = particle;
}

