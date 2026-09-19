#include "shared/particle.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/hash-functions.hlsl"

cbuffer TimeConstants : register(b0)
{
    float globalTime;
    float time;
    float runTime;
    float dummy;
    float lastFrameDuration;
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

cbuffer CountConstants : register(b2)
{
    int4 bufferCount;
}

cbuffer Params : register(b3)
{
    float filterEmitterId;
    float Frequency;
    float Amount;
    float Phase;
    float ParticleFriction;
    float Variation;
}


RWStructuredBuffer<Particle> Particles : u0;
RWStructuredBuffer<ParticleIndex> AliveParticles : u1;

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= bufferCount.x)
        return; // only check alive particles
    int index = AliveParticles[i.x].index;
    Particle p = Particles[index];
    if (filterEmitterId >= 0 && p.emitterId != filterEmitterId)
        return; // not the relevant emitter id to process

    float3 hash = hash31(index);

    // 2 lines below only relevant for sorting
    // float3 posInCamera = mul(Particles[i.x].position, ObjectToCamera).xyz; // todo: optimize
    // AliveParticles[index].squaredDistToCamera = posInCamera.z;//dot(-WorldToCamera[2].xyz, posInCamera);

    float3 v =  p.velocity; //float3(0,0,0)
    v += curlNoise((p.position + (hash - 0.5)*2 * Variation  + Phase ) * Frequency)* Amount;
    // v += curlNoise(Particles[i.x].position*0.0505);
    // v += curlNoise(Particles[i.x].position*1.505);
    p.velocity = v* (1- ParticleFriction);
    p.position += lastFrameDuration*(v.xyz);

    Particles[index] = p;
}

