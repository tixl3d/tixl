#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float Drag;
    float Speed; 
}

RWStructuredBuffer<Particle> Particles : u0; 

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    Particles.GetDimensions(numStructs, stride);
    if(i.x >= numStructs) 
        return;


    Particle p = Particles[i.x];
    float4 rot;
    //float v = q_separate_v(p.Rotation, rot);

    //float3 forward =  normalize(qRotateVec3(float3(0, 0, 1), rot));

    p.Position += p.Velocity * 0.01 * Speed;

    p.Velocity *= (1-Drag);
    // p.Rotation = q_encode_v(rot, v);

    Particles[i.x] = p;

}
