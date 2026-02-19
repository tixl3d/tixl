#include "shared/particle.hlsl"

cbuffer TimeConstants : register(b0)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

cbuffer CountConstants : register(b1)
{
    int4 bufferCount;
}

cbuffer Params : register(b2)
{
    float4 Color;
    float EmitPosY;
    float EmitPosYScatter;
    float Size;
    float Mass;
    float LifeTime;    
}


Texture2D<float4> inputTexture : register(t0);
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
    //if (i.x >= bufferCount.x)
    //    return; // no particles available

    ParticleIndex pi = DeadParticles.Consume();
        
    Particle particle = Particles[pi.index];
    uint rng_state = uint(runTime*1000.0) + i.x;

    float f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    float f1 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    float f2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    particle.position = float3(f0*200.0,f1*200.0,f2*200.0);
    float y = fmod(beatTime,4) * 100;
    float x = f0*200.0 + 100.0;
    particle.position = float3((x-100.0)*2.0, (y - 100.0)*2.0,f2*20.0);
    // particle.position = float3(f0*200.0,21.0,f2*20.0);

    f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    particle.lifetime = LifeTime;

    f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    f1 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    f2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0) - 0.5;
    particle.velocity = float3(f0*10.0, f1*10.0, f2*10.0);
    //particle.velocity = float3(0,0,0);

    // f0 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    // f1 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    // f2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    // particle.color = float4(f0,f1,f2,1.0);
    // particle.color = float4(1,0,0,1);
    particle.color = inputTexture[int2(4*int(x), 800-4*int(y))]*Color;
    //particle.color = float4(1,1,1,0.2);
    particle.color = Color;
    //particle.color.a = 1;
    Particles[pi.index] = particle;
}

