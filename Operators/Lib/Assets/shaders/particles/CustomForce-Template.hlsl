#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"
#include "shared/bias-functions.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer FloatParams : register(b1)
{
    float Amount;
    float NormalSamplingDistance;
    float SpeedFactor;
    float A;

    float3 Offset;
    float B;

    float C;
    float D;
    float2 GainAndBias;

    float4 Color;
}

cbuffer IntParams : register(b2)
{
    uint ParticleCount;
    int EnableBounce;
    int ApplyColorOnCollision;
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

Texture2D<float4> Gradient : register(t0);
Texture2D<float4> Image : register(t1);
RWStructuredBuffer<Particle> Particles : register(u0);

sampler ClampedSampler : s0;
sampler WrappedSampler : s1;

//=== Additional Resources ==========================================
/*{RESOURCES(t2)}*/

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/
//-------------------------------------------------------------------

float4 GetField(float4 p)
{
    float4 f = 1;
    /*{FIELD_CALL}*/
    return f;
}

inline float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}

//===================================================================

float3 GetNormal(float3 p, float offset=0.001)
{
    return normalize(
        GetDistance(p + float3(offset, -offset, -offset)) * float3(1, -1, -1) +
        GetDistance(p + float3(-offset, offset, -offset)) * float3(-1, 1, -1) +
        GetDistance(p + float3(-offset, -offset, offset)) * float3(-1, -1, 1) +
        GetDistance(p + float3(offset, offset, offset)) * float3(1, 1, 1));
}

float4 q_from_tangentAndNormal(float3 dx, float3 dz)
{
    dx = normalize(dx);
    dz = normalize(dz);
    float3 dy = -cross(dx, dz);

    float3x3 orientationDest = float3x3(
        dx,
        dy,
        dz);

    return normalize(qFromMatrix3Precise(transpose(orientationDest)));
}

float Biased(float f){return ApplyGainAndBias(f, GainAndBias);}
float4 SampleGradient(float f){return Gradient.SampleLevel(ClampedSampler, float2(f, 0.5), 0);}



[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);
    uint idx = i.x;
    if (idx >= maxParticleCount)
        return;

    Particle p = Particles[idx];
    float3 vel=p.Velocity;
    float3 pos=p.Position;  // Warning: avoid setting this, because it without applying SpeedFactor
    float4 col=p.Color;
{
//- METHOD -------------------------------------
/*{method}*/
//----------------------------------------------
}

    Particles[idx].Velocity = lerp(Particles[idx].Velocity, vel, Amount);
    Particles[idx].Color = lerp(Particles[idx].Color, col, Amount);
    Particles[idx].Position = lerp(Particles[idx].Position, pos, Amount);
}
