#include "shared/particle.hlsl"

static const float3 Quad[] = 
{
  float3(-1, -1, 0),
  float3( 1, -1, 0), 
  float3( 1,  1, 0), 
  float3( 1,  1, 0), 
  float3(-1,  1, 0), 
  float3(-1, -1, 0), 
};

cbuffer Transforms : register(b0)
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

cbuffer Params : register(b1)
{
    float size;
}

struct Output
{
    float4 position : SV_POSITION;
    float4 mask0 : MASK0;
    float4 mask1 : MASK1;
    float4 mask2 : MASK2;
    float4 mask3 : MASK3;
    float2 texCoord : TEXCOORD0;
};

StructuredBuffer<Particle> Particles : t0;
StructuredBuffer<ParticleIndex> AliveParticles : t1;

Output vsMain(uint id: SV_VertexID)
{
    Output output = (Output)0;

    int quadIndex = id % 6;
    int particleId = id / 6;
    float3 quadPos = Quad[quadIndex];
    Particle particle = Particles[AliveParticles[particleId].index];
    float4 particleQuadPosInCamera = mul(float4(particle.position,1), ObjectToCamera);
    particleQuadPosInCamera.xy += quadPos.xy*0.250;//*6.0;// * size;
    output.position = mul(particleQuadPosInCamera, CameraToClipSpace);
    float z = output.position.z;
    if (z < 0.25)
    {
        z *= 4.0;
        output.mask0 = clamp(floor(fmod(float4(z, z, z, z) + float4(1.0, 0.75, 0.50, 0.25), float4(1.25, 1.25, 1.25, 1.25))), float4(0, 0, 0, 0), float4(1, 1, 1, 1));
    }
    else if (z < 0.5)
    {
        z -= 0.25;
        z *= 4.0;
        output.mask1 = clamp(floor(fmod(float4(z, z, z, z) + float4(1.0, 0.75, 0.50, 0.25), float4(1.25, 1.25, 1.25, 1.25))), float4(0, 0, 0, 0), float4(1, 1, 1, 1));
    }
    else if (z < 0.75)
    {
        z -= 0.5;
        z *= 4.0;
        output.mask2 = clamp(floor(fmod(float4(z, z, z, z) + float4(1.0, 0.75, 0.50, 0.25), float4(1.25, 1.25, 1.25, 1.25))), float4(0, 0, 0, 0), float4(1, 1, 1, 1));
    }
    else
    {
        z -= 0.75;
        z *= 4.0;
        output.mask3 = clamp(floor(fmod(float4(z, z, z, z) + float4(1.0, 0.75, 0.50, 0.25), float4(1.25, 1.25, 1.25, 1.25))), float4(0, 0, 0, 0), float4(1, 1, 1, 1));
    }

    output.texCoord = (quadPos.xy * 0.5 + 0.5);

    return output;
}

