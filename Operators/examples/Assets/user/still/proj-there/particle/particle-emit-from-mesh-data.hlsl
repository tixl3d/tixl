#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/particle.hlsl"
#include "shared/point.hlsl"


cbuffer CountConstants : register(b0)
{
    int4 bufferCount;
};

cbuffer EmitParameter : register(b1)
{
    float EmitterId;
    float MaxEmitCount;
    float LifeTime;
    float EmitSize;
    float4 Color;
    float Seed;
};

cbuffer TimeConstants : register(b2)
{
    float GlobalTime;
    float Time;
    float RunTime;
    float BeatTime;
    float LastFrameDuration;
}


cbuffer Transforms : register(b3)
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

cbuffer MeshCounter : register(b4)
{
    int4 faceCount;
};

struct Face
{
    float3 positions[3];
    float2 texCoords[3];
    float3 normals[3];
    int id;
    float normalizedFaceArea;
    float cdf;
};

uint wang_hash(in out uint seed)
{
    seed = (seed ^ 61) ^ (seed >> 16);
    seed *= 9;
    seed = seed ^ (seed >> 4);
    seed *= 0x27d4eb2d;
    seed = seed ^ (seed >> 15);
    return seed;
}


StructuredBuffer<Face> PointCloud : t0;
Texture2D<float4> inputTexture : register(t1);

RWStructuredBuffer<Particle> Particles : u0;
ConsumeStructuredBuffer<ParticleIndex> DeadParticles : u1;
//RWStructuredBuffer<Face> PointCloud : u2;

SamplerState linearSampler : register(s0);


[numthreads(160,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    PointCloud.GetDimensions(numStructs, stride);
    if (i.x >= (uint)bufferCount.x)
        return; 

    if (i.x >= (uint)MaxEmitCount)
        return;

    uint bla = asint(BeatTime*1000) ^ i.x;
    uint rng_state = Seed < 0 ? (i.x*wang_hash(bla)) 
                              : (i.x*Seed); // todo hash12 with time as 2nd param
    float xi = (float(wang_hash(rng_state)) * (1.0 / 4294967296.0));

    // float3 hash = hash42(float2(BeatTime*1000, i.x*3*1000));
    // float xi = hash.x;

    uint cdfIndex = 0;
    while (cdfIndex < numStructs && xi > PointCloud[cdfIndex].cdf) // todo: make binary search
    {
        cdfIndex += 1;
    }

    uint index = cdfIndex;
    if (index >= (uint)faceCount)
        return;

    float xi1 = (float(wang_hash(rng_state)) * (1.0 / 4294967296.0));
    float xi2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);
    Face f = PointCloud[index];
    // if (f.positions[0].z < 0.1 || f.positions[1].z < 0.1 || f.positions[2].z < 0.1)
    //     return;
    float xi1Sqrt = sqrt(xi1);
    float u = 1.0 - xi1Sqrt;
    float v = xi2 * xi1Sqrt; 
    float w = 1.0 - u - v;
    float3 pos = f.positions[0] * u + f.positions[1] * v + f.positions[2] * w;

    ParticleIndex pi = DeadParticles.Consume();
    Particle particle = Particles[pi.index];


    //float scale = 2;
    particle.position = mul(float4(pos.xyz,1), ObjectToWorld);
    particle.emitterId = EmitterId;
    particle.lifetime = LifeTime;
    particle.emitTime = BeatTime;
    float size = 1.5;//EmitSize * Seed;
    particle.size = float2(size, size);
    particle.velocity = 0;//v0.normal*10;
    float2 texCoord = f.texCoords[0] * u + f.texCoords[1] * v + f.texCoords[2] * w;
    texCoord.y = 1.0 - texCoord.y;
    float4 color = float4(0.5, 0.5, 0.5, 1);//inputTexture.SampleLevel(linearSampler, texCoord, 0) * Color;
    particle.color = color;

    Particles[pi.index] = particle;
}

