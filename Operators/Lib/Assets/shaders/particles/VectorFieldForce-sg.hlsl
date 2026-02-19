#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    float Amount;
    float Variation;
}

cbuffer Params : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer Params : register(b2)
{
    uint ParticleCount;
    int EnableBounce;
}

RWStructuredBuffer<Particle> Particles : u0;
StructuredBuffer<PbrVertex> Vertices : t0;
StructuredBuffer<int3> Indices : t1;
sampler ClampedSampler : s0;

//=== Additional Resources ==========================================
/*{RESOURCES(t6)}*/

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

//===================================================================

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);
    int gi = i.x;
    if (gi >= maxParticleCount)
        return;

    if (isnan(Particles[gi].BirthTime))
        return;

    float3 pos = Particles[gi].Position;
    float4 f = GetField(float4(pos, 0));

    float3 variationFactor = hash11u(i.x) * Variation + 1;

    float3 velocity = f.xyz * Amount * f.w * variationFactor;

    if (!isnan(velocity.x) && !isnan(velocity.y) && !isnan(velocity.z))
        Particles[gi].Velocity += velocity;
}
