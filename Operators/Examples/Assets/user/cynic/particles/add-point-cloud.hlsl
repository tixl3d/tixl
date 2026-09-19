
#include "shared/particle.hlsl"

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

cbuffer CountConstants : register(b2)
{
    int4 bufferCount;
}


cbuffer Params : register(b3)
{
    // eventual parameters go here
}

struct LegacyPoint
{
    float3 position;
    int id;
    float4 color;
};

StructuredBuffer<LegacyPoint> PointCloud : s0;

RWStructuredBuffer<Particle> Particles : u0;
ConsumeStructuredBuffer<ParticleIndex> DeadParticles : u1;

[numthreads(160,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= bufferCount.x)
        return; // no particles available

    float3 direction = float3(1, 0, 0);
    ParticleIndex pi = DeadParticles.Consume();
        
    Particle particle = Particles[pi.index];
    LegacyPoint aPoint = PointCloud[i.x];

    particle.size = 1;
    particle.position = aPoint.position;
    particle.emitterId = 0; //aPoint.id;
    particle.lifetime = 100.0;
    particle.emitTime = BeatTime;
    particle.velocity = float3(0,0,0);
    particle.color = aPoint.color; //float4(1,1,1,1);

    Particles[pi.index] = particle;
}

