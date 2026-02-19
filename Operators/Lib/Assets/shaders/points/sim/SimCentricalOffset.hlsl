#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 Center;
    float MaxAcceleration;
    float Acceleration;
    float DecayExponent;
}

RWStructuredBuffer<Point> Points : u0;

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    float3 pos = Points[i.x].Position;
    // float4 rot = Points[i.x].Rotation;

    float3 d = pos - Center;

    float distance = length(d);
    if (distance < 0.001)
        return;

    float3 dNormalized = d / distance;

    float force = clamp(Acceleration / pow(distance, DecayExponent), -MaxAcceleration, MaxAcceleration);
    float3 offset = normalize(dNormalized) * force;

    Points[i.x].Position += offset;

    // Points[i.x].Velocity += force;
    // float4 normalizedRot;
    // float v = q_separate_v(rot, normalizedRot);
    // float3 forward = qRotateVec3(float3(0,0, v), normalizedRot);
    // forward -= normalize(direction) * force;
    // float newV = length(forward);
    // float4 newRotation = qLookAt(normalize(Points[i.x].Velocity), float3(0,0,1));
    // Points[i.x].Rotation = q_encode_v(newRotation, newV);
}