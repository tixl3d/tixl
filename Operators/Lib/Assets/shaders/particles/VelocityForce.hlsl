#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer Params : register(b0)
{
    float Amount;
    float Acelleration;
    float MinSpeed;
    float MaxSpeed;

    float Variation;
    float2 VariationGainAndBias;
}

RWStructuredBuffer<Particle> Particles : u0; 

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);
    if(i.x >= maxParticleCount) 
        return;

    float3 velocity = Particles[i.x].Velocity;
    float speed = length(velocity);

    // Rather do nothing... 
    if(speed < 0.0001) 
        return;    

    float3 dir = normalize(velocity); 

    float rand = hash11u(i.x);
    float randBias = ApplyGainAndBias(rand, VariationGainAndBias);
    float3 variationFactor = (1 - (1-randBias) * saturate(Variation));

    float strength = Amount * variationFactor;

    speed += Acelleration * 0.02 * strength;
    speed = clamp(speed, MinSpeed, MaxSpeed);

    Particles[i.x].Velocity = dir * speed;
}

