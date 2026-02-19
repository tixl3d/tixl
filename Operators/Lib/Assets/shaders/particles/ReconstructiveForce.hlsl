#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformVolume;

    float FallOff;
    float Bias;
    float Strength;
    float DistanceMode;

    float SpeedFactor;
}

StructuredBuffer<Point> TargetPoints : t0;
RWStructuredBuffer<Particle> Particles : u0;

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint gi = i.x;
    uint targetPointCount, maxParticleCount, stride;
    Particles.GetDimensions(maxParticleCount, stride);
    if (gi >= maxParticleCount)
        return;

    if (isnan(TransformVolume._11) || TransformVolume._11 == 0)
        return;

    TargetPoints.GetDimensions(targetPointCount, stride);
    uint targetPointIndex = gi % targetPointCount;

    float3 pos = Particles[gi].Position;
    float4 rot = Particles[gi].Rotation;
    float3 velocity = Particles[gi].Velocity;

    float3 usedPos = DistanceMode < 0.5 ? pos : TargetPoints[gi].Position;
    float3 posInVolume = mul(float4(usedPos, 1), TransformVolume).xyz;
    float d = length(posInVolume);
    const float r = 0.5;
    float t = (d + r * (FallOff - 1)) / (2 * r * FallOff);

    // float blendFactor = smoothstep(1, 0, t) * Strength * SpeedFactor;

    float baseBlend = smoothstep(1, 0, t) * Strength;
    float blendFactor = 1 - pow(1 - baseBlend, SpeedFactor);

    Particles[gi].Position = lerp(pos, TargetPoints[targetPointIndex].Position, blendFactor);
    Particles[gi].Rotation = qSlerp(rot, TargetPoints[targetPointIndex].Rotation, blendFactor);
    Particles[gi].Velocity = lerp(velocity, 0, blendFactor);
    Particles[gi].Color = lerp(Particles[gi].Color, TargetPoints[targetPointIndex].Color, blendFactor);

    // Particles[gi].Radius = lerp(Particles[gi].Radius, TargetPoints[targetPointIndex].W, blendFactor);
}
