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

cbuffer ShadowTransforms : register(b1)
{
    float4x4 Shadow_CameraToClipSpace;
    float4x4 Shadow_ClipSpaceToCamera;
    float4x4 Shadow_WorldToCamera;
    float4x4 Shadow_CameraToWorld;
    float4x4 Shadow_WorldToClipSpace;
    float4x4 Shadow_ClipSpaceToWorld;
    float4x4 Shadow_ObjectToWorld;
    float4x4 Shadow_WorldToObject;
    float4x4 Shadow_ObjectToCamera;
    float4x4 Shadow_ObjectToClipSpace;
};

struct Output
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float4 color : COLOR;
    float4 world_P : POSITION;
};

StructuredBuffer<Particle> Particles : t0;
StructuredBuffer<ParticleIndex> AliveParticles : t1;

Texture2D<float4> inputTexture : register(t2);
sampler texSampler : register(s0);

Output vsMain(uint id: SV_VertexID)
{
    Output output;

    int quadIndex = id % 6;
    int particleId = id / 6;
    float3 quadPos = Quad[quadIndex];
    Particle particle = Particles[AliveParticles[particleId].index];
    float4 quadPosInCamera = mul(float4(particle.position,1), ObjectToCamera);
    quadPosInCamera.xy += quadPos.xy*0.250;//*6.0;// * size;
    output.position = mul(quadPosInCamera, CameraToClipSpace);
    output.world_P = mul(float4(particle.position, 1), ObjectToWorld);

    output.color = particle.color;
    output.texCoord = (quadPos.xy * 0.5 + 0.5);

    return output;
}

