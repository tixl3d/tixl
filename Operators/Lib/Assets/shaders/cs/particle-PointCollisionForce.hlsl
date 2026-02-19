#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
 
RWStructuredBuffer<Particle> particles :register(u0);

cbuffer Params : register(b0)
{
    float CellSize;
    float Bounciness;
    float Attraction;
    float SpeedFactor;
    float AttractionDecay;
    float CollisionResolve;
}

#include "points/spatial-hash-map/spatial-hash-map-lookup.hlsl"

static const float3 CellOffsets[] = 
{
    float3(0,0,0) - 0.5,
    float3(0,0,1) - 0.5,
    float3(0,1,0) - 0.5,
    float3(0,1,1) - 0.5,
    float3(1,0,0) - 0.5,
    float3(1,0,1) - 0.5,
    float3(1,1,0) - 0.5,
    float3(1,1,1) - 0.5,
};


[numthreads( 16, 1, 1 )]
void DispersePoints(uint3 DTid : SV_DispatchThreadID, uint GI: SV_GroupIndex)
{
    uint gi = DTid.x;

    uint pointCount, stride;
    particles.GetDimensions(pointCount, stride);
        
    if(gi >= pointCount)
        return; // out of bounds
    
    Particle p = particles[gi];

    float3 position = p.Position;
    uint startIndex, endIndex;

    int closestIndex = -1;
    float3 closestDirection = 0;
    float closestDistance = 9999999; 

    float3 forceSum =0;
    int count = 0;

    float3 pos = particles[gi].Position;
    float3 velocity = particles[gi].Velocity;
    float3 posNext = pos + velocity * SpeedFactor * 0.01;
    float r = particles[gi].Radius;

    for(int cellOffsetIndex =0; cellOffsetIndex < 8; cellOffsetIndex++) 
    {
        float3 cellOffset = CellOffsets[cellOffsetIndex] * CellSize;

        if(GridFind(position + cellOffset, startIndex, endIndex))   
        {
            const uint particleCount = endIndex - startIndex;
            float3 sumForces = 0;

            endIndex = min(startIndex + 64 , endIndex);

            for(uint i=startIndex; i < endIndex; ++i) 
            {
                uint otherIndex = CellPointIndices[i];

                if( otherIndex == gi)
                    continue;

                float3 otherPos = particles[otherIndex].Position;
                float r2 = particles[otherIndex].Radius;

                float3 pToO = pos - otherPos;
                float centerDistance = length(pToO);
                if(centerDistance < 0.0001) 
                {
                    pToO = hash41u(gi).xyz * float3(1,1,0) * 0.01;
                    centerDistance = length(pToO);
                }
                float3 direction = pToO / centerDistance;
                float gap = centerDistance - r2 - r;
                float gapNext = length(posNext - otherPos) - r2 - r;


                float massRatio = (r) / (r2);
                if(sign( gap * gapNext) < 0  && gap > 0) 
                {
                    velocity = reflect(velocity, direction );
                    particles[gi].Velocity = velocity * Bounciness;
                }  
                else 
                {
                    // Already inside...
                    if(gap < 0) 
                    {
                        particles[gi].Position -= direction * gap * CollisionResolve;

                    }
                    // Attraction?
                    else 
                    {           
                        particles[gi].Velocity -= (direction * Attraction  / massRatio) * (1/( pow(centerDistance, AttractionDecay))) ;
                    }
                }   

            }
        }
    }

} 