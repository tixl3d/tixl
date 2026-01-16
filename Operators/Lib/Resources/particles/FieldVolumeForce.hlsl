#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer Params : register(b1)
{
    float Amount;
    float Attraction;
    float AttractionDecay;
    float Repulsion;

    float Bounciness;
    float RandomizeBounce;
    float RandomizeReflection;
    float InvertVolumeFactor;

    float NormalSamplingDistance;
    float SpeedFactor;
}

cbuffer Params : register(b2)
{
    uint ParticleCount;
    int EnableBounce;
    int ApplyColorOnCollision;
}

RWStructuredBuffer<Particle> Particles : u0;
// StructuredBuffer<PbrVertex> Vertices : t0;

// StructuredBuffer<int3> Indices : t1;

sampler ClampedSampler : s0;
sampler WrappedSampler : s1;

//=== Additional Resources ==========================================
/*{RESOURCES(t0)}*/

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

float3 GetNormal(float3 p, float offset)
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

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);
    int gi = i.x;
    if (gi >= maxParticleCount)
        return;

    // if (isnan(Particles[gi].BirthTime))
    //     return;

    float3 pos = Particles[gi].Position;
    float distance = GetDistance(pos);
    float3 surfaceN = GetNormal(pos, NormalSamplingDistance);

    float4 rot = Particles[gi].Rotation;
    float3 velocity = Particles[gi].Velocity;
    float3 posNext = float4(pos + velocity * SpeedFactor * 0.01 * 2, 1);
    float distanceNext = GetDistance(posNext);

    float3 force = 0;
    surfaceN *= InvertVolumeFactor;

    // Reflect if distance changes
    if (sign(distance * distanceNext) < 0 && distance * InvertVolumeFactor > 0)
    {
        float4 rand = hash41u(gi);
        float3 v = lerp(velocity,
                        reflect(velocity, surfaceN + (RandomizeReflection * (rand.xyz - 0.5))),
                        EnableBounce);

        velocity = lerp(velocity,
                        //
                        (v * Bounciness     //
                         * (RandomizeBounce //
                                * (rand.z - 0.5) +
                            1)), //
                        Amount);

        if (ApplyColorOnCollision)
        {
            float4 surfaceColor = GetField(float4(pos, 1));
            Particles[gi].Color.rgb = surfaceColor.rgb;
        }
    }
    else
    {
        if (distance * InvertVolumeFactor < 0)
        {
            force = surfaceN * Repulsion;
        }
        else
        {
            force = -surfaceN * Attraction / (1 + distance * AttractionDecay);
        }
        velocity += force * Amount * SpeedFactor;
    }

    if (!isnan(velocity.x) && !isnan(velocity.y) && !isnan(velocity.z))
        Particles[gi].Velocity = velocity;
}
