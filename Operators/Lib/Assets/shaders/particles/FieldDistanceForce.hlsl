#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    float Amount;
    float Attraction;
    float Repulsion;
    float NormalSamplingDistance;
    float DecayWithDistance;
}

cbuffer Params : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer Params : register(b2)
{
    uint ParticleCount;
}

RWStructuredBuffer<Particle> Particles : u0;
// StructuredBuffer<int3> Indices : t1;
//=== Globals =======================================================
/*{GLOBALS}*/

//=== Resources =====================================================
/*{RESOURCES(t0)}*/

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

float3 GetFieldNormal(float3 p)
{
    return normalize(
        GetDistance(p + float3(NormalSamplingDistance, -NormalSamplingDistance, -NormalSamplingDistance)) * float3(1, -1, -1) +
        GetDistance(p + float3(-NormalSamplingDistance, NormalSamplingDistance, -NormalSamplingDistance)) * float3(-1, 1, -1) +
        GetDistance(p + float3(-NormalSamplingDistance, -NormalSamplingDistance, NormalSamplingDistance)) * float3(-1, -1, 1) +
        GetDistance(p + float3(NormalSamplingDistance, NormalSamplingDistance, NormalSamplingDistance)) * float3(1, 1, 1));
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= ParticleCount)
        return;

    Particle p = Particles[i.x];

    float3 pos = p.Position;
    float3 n = GetFieldNormal(pos);
    float d = GetDistance(pos);

    if (isnan(d) || isnan(n.x))
        return;

    // Attract outside
    if (d > 0)
    {
        float decay = pow(d + 1, -DecayWithDistance);
        p.Velocity -= n * Attraction * Amount * decay;
    }
    // Repell inside
    else
    {
        p.Velocity += n * Repulsion * Amount;
    }

    Particles[i.x] = p;
}
