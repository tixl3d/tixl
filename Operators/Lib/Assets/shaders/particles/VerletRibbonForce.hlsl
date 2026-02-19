#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 Gravity;
    float Strength;

    float SegmentLength;
    float DeltaTime;
    float RestoreFactor;
    float Damping;
}

cbuffer Params : register(b1)
{
    int IsReset;
}



// 48 bytes
struct VerletAttr
{
    float3 PreviousPosition;
    float1 __padding1;
    float3 Pos1;
    float1 __padding2;
    float3 Pos2;
    float1 __padding3;
};

// 48 bytes
struct UpdateCountAttr
{
    uint Count;
};


StructuredBuffer<LegacyPoint> ReferencePoints : t0;   

RWStructuredBuffer<Particle> Particles : u0; 
RWStructuredBuffer<VerletAttr> VerletAttributes : u1; 
RWStructuredBuffer<UpdateCountAttr> UpdateCount : u2; 


inline bool IsPinned(uint index)
{
    return index == 0 || isnan(ReferencePoints[index-1].Stretch.x);
}


[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x;
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);

    if(index >= maxParticleCount)
        return;


    // Initialize previous position with current
    if(IsReset > 0) 
    {
        float3 pos = Particles[index].Position;
        VerletAttributes[index].PreviousPosition = pos;
 
        if(index == 0)
            UpdateCount[0].Count =0;

        return;
    }

    // Integration
    if(!IsPinned(index)) 
    {
        float3 pos = Particles[index].Position;

        // Verlet Integration
        float3 velocity = pos - VerletAttributes[index].PreviousPosition;
        float3 newPos = pos + velocity * Damping + Gravity * DeltaTime * DeltaTime;
        Particles[index].Position = newPos;

        VerletAttributes[index].PreviousPosition = pos;
    }

    VerletAttributes[index].Pos1 = Particles[index].Position;
    VerletAttributes[index].Pos2 = Particles[index].Position;
}

[numthreads(256,1,1)]
void constraints(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x;
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);

    if(index >= maxParticleCount)
        return;

    //AllMemoryBarrier();        

    if(index == 0)
        UpdateCount[0].Count++;

    AllMemoryBarrier();     
    //AllMemoryBarrier(); 

    uint indexA = index + (UpdateCount[0].Count % 2);
    uint indexB = index  + ((UpdateCount[0].Count +1) % 2);

    
    if( isnan( ReferencePoints[indexA].Stretch.x * ReferencePoints[indexB].Stretch.x))
        return;

    bool read1 = (UpdateCount[0].Count % 2) == 0;

    // float3 posA = VerletAttributes[indexA].PreviousPosition;
    // float3 posB = VerletAttributes[indexB].PreviousPosition;


    // float3 posA = Particles[indexA].Position;
    // float3 posB = Particles[indexB].Position;
    float3 posA = read1 ? VerletAttributes[indexA].Pos1 : VerletAttributes[indexA].Pos2;
    float3 posB = read1 ? VerletAttributes[indexB].Pos1 : VerletAttributes[indexB].Pos2;

    float3 delta = posB - posA;
    float distance = length(delta);
    
    float difference = distance < 0.0001 ? 0 : (distance - SegmentLength) / (distance);

    // Adjust positions
    if (!IsPinned(indexA)) {
        posA += delta * 0.5 * difference;// * Strength;
        //Particles[indexA].Position = posA;
    }

    if(read1)
        VerletAttributes[indexA].Pos2 = posA;
    else
        VerletAttributes[indexA].Pos1 = posA;


    if (!IsPinned(indexB)) {
        posB -= delta * 0.5 * difference;// * Strength;
        //Particles[indexB].Position = posB;
    }
    if(read1)
        VerletAttributes[indexB].Pos2 = posB;
    else
        VerletAttributes[indexB].Pos1 = posB;
    
}


[numthreads(64,1,1)]
void apply(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x;
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);

    if(index >= maxParticleCount)
        return;

    bool read1 = (UpdateCount[0].Count % 2) == 0;

    float3 pos = lerp ( 
        Particles[index].Position, 
        (read1 
        ? VerletAttributes[index].Pos2 
        : VerletAttributes[index].Pos1), 
        Strength);

    pos = lerp ( pos, ReferencePoints[index].Position, RestoreFactor);
    Particles[index].Position = pos;

}