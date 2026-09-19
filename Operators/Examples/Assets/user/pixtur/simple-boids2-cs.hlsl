/* An update version of the boid system using a spatial hash map */

#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "points/spatial-hash-map/hash-map-settings.hlsl" 

cbuffer ParamConstants : register(b0)
{
    float EffectLayer;
    float GridCellSize;
    float WrapAround;
    float Jitter;

    float Time;
    
}


struct Boid
{
    float CohesionRadius;
    float CohesionDrive;
    float AlignmentRadius;
    float AlignmentDrive;
    float SeparationRadius;
    float SeparationDrive;
    float MaxSpeed;
    float _padding;
};

struct Agent {
    float3 Position;
    float BoidType;
    float4 SpriteOrientation;
};

static const float3 Offsets[] =  
{
  float3(0, 0, 0),
  float3(1, 0, 0),
  float3(0, 1, 0), 
  float3(1, 1, 0), 
};


#define mod(x,y) ((x)-(y)*floor((x)/(y)))

sampler texSampler : register(s0);

StructuredBuffer<uint> CellPointIndices :register(t0);     // IndexToPointBuffer -> CellPointIndices
StructuredBuffer<uint2> PointCellIndices :register(t1);    // CellIndicesBuffer -> PointCellIndices
StructuredBuffer<uint> HashGridCells :register(t2);        // HashGridBuffer -> HashGridCells
StructuredBuffer<uint> CellPointCounts :register(t3);      // CountBuffer -> CellPointCounts
StructuredBuffer<uint> CellRangeIndices :register(t4);     // RangeIndexBuffer -> CellRangeIndices
 
StructuredBuffer<Boid> BoidsTypes : register(t5);
Texture2D<float4> InputTexture : register(t6);

RWStructuredBuffer<Agent> Agents : register(u0);

static const float3 FORWARD = float3(0,1,0);
static const float3 UP = float3(0,0,1);


bool GridFind(in float3 position, out uint startIndex, out uint endIndex)
{
    uint i;
    position+= 100 * GridCellSize;
    int3 cell = int3(position / GridCellSize);
    uint cellIndex = (pcg(cell.x + pcg(cell.y + pcg(cell.z))) % ParticleGridCellCount);
    uint hashValue = max(xxhash(cell.x + xxhash(cell.y + xxhash(cell.z))), 1);
    uint cellBegin = cellIndex * ParticleGridEntryCount;
    uint cellEnd = cellBegin + ParticleGridEntryCount;
    for(i = cellBegin; i < cellEnd; ++i)
    {
        const uint entryValue = HashGridCells[i];
        if(entryValue == hashValue)
            break;  // found existing entry

        if(entryValue == 0)
            i = cellEnd;
    }
    if(i >= cellEnd)
        return false;

    startIndex = CellRangeIndices[i];
    int count = min(CellPointCounts[i], 50);

    endIndex = startIndex + count;
    return true;
} 


[numthreads(256,1,1)]
void main(uint3 Gid : SV_GroupID, uint3 DTid : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID, uint Gi : SV_GroupIndex)
{
    uint pointCount, stride;
    Agents.GetDimensions(pointCount, stride);
        
    if(DTid.x >= pointCount)
        return; // out of bounds

    // Setup Buffers
    float3 centerForCohesion;
    int countForCohesion =0;

    float3 centerForSeparation;
    int countForSeparation =0;

    float3 averageDirection;
    int countForAlignment =0;

    int pointIndex = DTid.x;
    Agent self = Agents[pointIndex];

    // Rotate back

    float3 direction = 0;
    float3 position = self.Position;
    
    if(true) {
        direction = rotate_vector(FORWARD, self.SpriteOrientation); 
    }
    else {
        direction = float3(rotate_vector(FORWARD, self.SpriteOrientation).xy, 0); 
        position.z = 0;
    }
    
    
    int boidTypIndex = 0;

    int startIndex, endIndex;

    float3 lookupPos = position;
    int foundNeighbours = 0;

    float3 jitter = (hash13(Time* 123.3 % 421) -0.5) * GridCellSize * 0.2;
    jitter.z = 0;
    float3 jitteredPosition = position + jitter;

    float3 posInCel = mod(jitteredPosition, GridCellSize) - GridCellSize /2;
    float3 sign = posInCel < 0 ? -1 : 1;

    for(uint offsetIndex = 0; offsetIndex < 4; offsetIndex++) 
    {

        lookupPos = jitteredPosition + Offsets[offsetIndex] * GridCellSize * sign;

        if(GridFind(lookupPos, startIndex, endIndex)) 
        {            
            for(uint i=startIndex; i < endIndex; ++i) 
            {
                uint otherIndex = CellPointIndices[i];
                if(otherIndex == pointIndex)
                    continue;

                float3 otherPos = Agents[otherIndex].Position;
                float distance =  length(otherPos - position);

                if(distance < BoidsTypes[boidTypIndex].AlignmentRadius)
                {
                    averageDirection += rotate_vector(FORWARD, Agents[otherIndex].SpriteOrientation);
                    countForAlignment++;
                }

                if(distance < BoidsTypes[boidTypIndex].CohesionRadius)
                {
                    centerForCohesion += Agents[otherIndex].Position;
                    countForCohesion++;
                }

                if(distance < BoidsTypes[boidTypIndex].SeparationRadius)
                {
                    centerForSeparation += Agents[otherIndex].Position;
                    countForSeparation++;
                }
                foundNeighbours++;
            }
            
        }
    }

    // Aligment
    if(countForAlignment > 0) 
    {
        averageDirection /= countForAlignment;
        float l = length(averageDirection);
        if(l > 0.0001) {
            direction = lerp(direction, averageDirection/l, BoidsTypes[boidTypIndex].AlignmentDrive);
        }
    }

    // Separation
    if(countForSeparation > 0) 
    {
        centerForSeparation /= countForSeparation;        
        float3 toSeparation = position - centerForSeparation;
        float lenToSeparation = length(position - centerForSeparation);
        if(lenToSeparation > 0.0001) {
            direction = lerp(direction, toSeparation / lenToSeparation, BoidsTypes[boidTypIndex].SeparationDrive );
        }
    }

    // Cohesion
    if(countForCohesion > 0) 
    {
        centerForCohesion /= countForCohesion;        
        float3 toCohesion = -(position - centerForCohesion);
        float lenToCohesion = length(position - centerForCohesion);
        if(lenToCohesion > 0.0001) {
            direction = lerp(direction, toCohesion / lenToCohesion, BoidsTypes[boidTypIndex].CohesionDrive );
        }
    }

    // Effect Texture
    // float2 uv= (position.xy * 0.5) +0.5;
    // uv = float2(uv.x, 1- uv.y);
    // float4 c = InputTexture.SampleLevel(texSampler, uv, 0);
    // direction.xy -= c.xy * EffectLayer;

    // float len = length(direction);
    // if(isnan(len) || len == 0) 
    // {
    //      direction = float3(-1,-1,0);
    // }
    // else 
    // {
    //     direction /= len;
    // }
    
    float len = length(direction);
    direction /= len;
    position += direction * BoidsTypes[boidTypIndex].MaxSpeed / 60;


    if(WrapAround) 
    {
        position = mod(position + 1, 2) - 1;
    }

    
    //float4 rot = Agents[DTid.x].SpriteOrientation;
    
    // Use look at velocity rotation and rotate back into xy plane
    float4 rot = normalize(q_look_at(direction, float3(0,0,1)));
    rot = qmul(rot, rotate_angle_axis(0.5*PI , float3(1,0,0)));
    //rot = q_slerp(self.SpriteOrientation, rot, 0.9);

    // 2d-rotation around z
    Agents[DTid.x].SpriteOrientation = rot;
    Agents[DTid.x].Position = position;
    //Agents[DTid.x].Position += float3(0,0.001,0);
}