#include "shared/particle.hlsl"
#include "shared/noise-functions.hlsl"

cbuffer TimeConstants : register(b0)
{
    float GlobalTime;
    float Time;
    float RunTime;
    float BeatTime;
    float LastFrameDuration;
}

cbuffer Transforms : register(b1)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};


RWStructuredBuffer<Particle> Particles : u0;
RWStructuredBuffer<ParticleIndex> AliveParticles : u1;
AppendStructuredBuffer<ParticleIndex> DeadParticles : u2;
RWBuffer<uint> IndirectArgs : u3;

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= bufferCount.x)
        return; // only check alive particles

    // Initialize the draw args using the first thread in the Dispatch call
    if (i.x == 0)
    {
        IndirectArgs[0] = 0;	// Number of primitives reset to zero
        IndirectArgs[1] = 1;	// Number of instances is always 1
        IndirectArgs[2] = 0;
        IndirectArgs[3] = 0;
    }

    // Wait after draw args are written so no other threads can write to them before they are initialized
    GroupMemoryBarrierWithGroupSync();

    float oldLifetime = Particles[i.x].lifetime;
    float newLifetime = oldLifetime - LastFrameDuration;

    if (newLifetime < 0.0)
    {
        if (oldLifetime >= 0.0)
        {
            ParticleIndex pi;
            pi.index = i.x;
            pi.squaredDistToCamera = 99999.0;
            DeadParticles.Append(pi);
        }
    }
    else
    {
        uint index = AliveParticles.IncrementCounter();
        AliveParticles[index].index = i.x;

        // 2 lines below only relevant for sorting
        // float3 posInCamera = mul(Particles[i.x].position, ObjectToCamera).xyz; // todo: optimize
        // AliveParticles[index].squaredDistToCamera = posInCamera.z;//dot(-WorldToCamera[2].xyz, posInCamera);

        uint originalValue;
        InterlockedAdd(IndirectArgs[0], 6, originalValue);
    }

    Particles[i.x].lifetime = newLifetime;
}

