#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    float Amount;
    float Frequency;
    float Phase;
    float Variation;

    float SpeedFactor;
}

cbuffer Params : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

RWStructuredBuffer<Particle> Particles : u0;

sampler ClampedSampler : register(s0);

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

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);
    if (i.x >= maxParticleCount)
    {
        return;
    }

    float3 variationOffset = hash41u(i.x).xyz * Variation;
    float3 pos = Particles[i.x].Position * 0.9; // avoid simplex noice glitch at -1,0,0
    float3 noiseLookup = (pos + variationOffset + Phase * float3(1, -1, 0)) * Frequency;
    float3 velocity = Particles[i.x].Velocity;
    // float speed = length(velocity);

    float4 field = GetField(float4(pos, 0));
    float fieldAmount = (field.r + field.g + field.b) / 3;

    float amount = Amount / 100 * fieldAmount * (SpeedFactor);
    Particles[i.x].Velocity = velocity + curlNoise(noiseLookup) * amount;
}
