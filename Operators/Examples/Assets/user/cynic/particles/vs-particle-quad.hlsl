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
    float4 Color;
    float Size;
    float3 LightPosition;
    float LightIntensity;
    float LightDecay;
    float RoundShading;
    float NearPlane;
};


cbuffer TimeConstants : register(b2)
{
    float GlobalTime;
    float Time;
    float RunTime;
    float BeatTime;
}
struct Output
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
    float3 objectPos: POSITIONT;
    float3 posInWorld: POSITION2;
    float3 velocity: POSITION3;
};

sampler texSampler : register(s0);
StructuredBuffer<Particle> Particles : t0;
StructuredBuffer<ParticleIndex> AliveParticles : t1;

Texture2D<float4> colorOverLifeTime : register(t2);
Texture2D<float4> colorForDirection : register(t3);

Output vsMain(uint id: SV_VertexID)
{
    Output output;



    int quadIndex = id % 6;
    int particleId = id / 6;
    float3 quadPos = Quad[quadIndex];

    Particle particle = Particles[AliveParticles[particleId].index];
    float4 quadPosInCamera = mul(float4(particle.position,1), ObjectToCamera);



    float4 particleInCamera = mul(float4(particle.position,1), ObjectToCamera);
    float nearDistancePlane = -NearPlane;
    float notTooCloseFactor = 1-smoothstep(nearDistancePlane, nearDistancePlane + NearPlane , particleInCamera.z);

    float distanceToLight = length(LightPosition - particle.position);
    output.color = particle.color * Color;
    output.color.rgb *= LightIntensity * pow( distanceToLight + 1, -LightDecay);

    float normalizedAge = saturate( (BeatTime - particle.emitTime) / particle.lifetime);
    float4 colorForLifeTime = colorOverLifeTime.SampleLevel(texSampler, float2(normalizedAge,0), 0);
    output.color.rgba *= colorForLifeTime;

    

    
    float scale = notTooCloseFactor * saturate(BeatTime - particle.emitTime) * saturate(particle.lifetime)  * particle.size  * output.color.a * Size;// HACK
    quadPosInCamera.xy += quadPos.xy*0.050  * scale;  // * (sin(particle.lifetime) + 1)/20;//*6.0;// * size;
    output.position = mul(quadPosInCamera, CameraToClipSpace);
    output.posInWorld = mul(quadPosInCamera, CameraToWorld).xyz;
    output.color.a = 1;
    
    float3 normalizedVelocity = normalize(particle.velocity);
    float u = 1-((atan2(normalizedVelocity.y, normalizedVelocity.z) + 3.1415) / (3.1415 *2));
    float v = normalizedVelocity.z/2 + 0.5;
    output.color.rgb *=  colorForDirection.SampleLevel(texSampler, float2(v,u), 0);;

    //output.color.r = sin(particle.lifetime);
    //output.color.gb =0; 


    //output.color.gb = 1;
    output.texCoord = (quadPos.xy * 0.5 + 0.5);
    output.objectPos = particle.position;
    float4 velocity = mul(float4(particle.velocity, 0), ObjectToClipSpace);
    output.velocity.xyz = velocity.xyz / velocity.w;
    return output;
}

