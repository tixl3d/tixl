#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
 
RWStructuredBuffer<Particle> particles :register(u0);

cbuffer Params : register(b0)
{
    float Threshold;
    float Dispersion;
    float CellSize;
    float ClampAccelleration;
    float Time;
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
    uint pointCount, stride;
    particles.GetDimensions(pointCount, stride);
        
    if(DTid.x >= pointCount)
        return; // out of bounds
    
    Particle p = particles[DTid.x];

    float3 position = p.Position;
    //float3 searchPos = position; 

    uint startIndex, endIndex;

    int closestIndex = -1;
    float3 closestDirection = 0;
    float closestDistance = 9999999; 

    float4 rot;
    //float v = q_separate_v(p.Rotation, rot);
    float3 orgV = p.Velocity;

    float3 forceSum =0;

    int count = 0;

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
                uint pointIndex = CellPointIndices[i];

                if( pointIndex == DTid.x)
                    continue;

                float3 otherPos = particles[pointIndex].Position;
                float3 direction = position - otherPos;
                float distance = length(direction);
                direction /= distance;

                float wA = p.Radius;
                float wB = particles[pointIndex].Radius;
                float massRatio = (wB * wB) / (wA * wA);
                //float massRatio = (wB) / (wA);
            
                float gapDistance = distance - wA - wB;
                float f = gapDistance < 0 ?  -gapDistance*100
                                          :   -clamp(1 * smoothstep(0,1, gapDistance * 1) / (1+ gapDistance * 10), -1, 1);
                
                forceSum += direction * f * massRatio;
                count++;
            }
        }
    }

    if(count > 0) 
    {
        forceSum *= 0.1 / p.Radius;
        forceSum += orgV;

        float forceMag =  clamp(length(forceSum),0, 10);
        float3 forceDirection =  forceSum / (forceMag + 0.0001);
        float angle = atan2(forceSum.x, forceSum.y);
        float4 newRot = qFromAngleAxis(angle, -float3(0,0,1));
        newRot = qMul(newRot, qFromAngleAxis(-PI/2, float3(1,0,0)));
        particles[DTid.x].Rotation = newRot;
        particles[DTid.x].Velocity = forceSum; // needs validation...

        //particles[DTid.x].rotation = q_encode_v(newRot, forceMag);
        return;
    }
} 