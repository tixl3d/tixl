#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
 
RWStructuredBuffer<LegacyPoint> points :register(u0);

cbuffer Params : register(b0)
{
    float Threshold;
    float Dispersion;
    float CellSize;
    float ClampAccelleration;
    float Time;
}

#include "points/spatial-hash-map/spatial-hash-map-lookup.hlsl"


[numthreads( 16, 1, 1 )]
void DispersePoints(uint3 DTid : SV_DispatchThreadID, uint GI: SV_GroupIndex)
{
    uint pointCount, stride;
    points.GetDimensions(pointCount, stride);
        
    if(DTid.x >= pointCount)
        return; // out of bounds

    float3 position = points[DTid.x].Position;
    //float3 jitter = (hash33u( uint3(DTid.x, DTid.x + 134775813U, DTid.x + 1664525U) + Time % 123.12 ) -0.5f)  * CellSize * 1;
    float3 jitter = (hash33u( uint3(DTid.x, DTid.x + 134775813U, DTid.x + 1664525U + (int)(Time * 123.12)) ) -0.5f)  * CellSize * 2;
    //float3 jitter = (hash31( Time * 1234.37) - 0.5f) * CellSize * 1;
    float3 searchPos = position + jitter;

    uint startIndex, endIndex;
    if(GridFind(searchPos, startIndex, endIndex)) 
    {
        const uint particleCount = endIndex - startIndex;
        int count =0;
        float3 sumForces = 0;

        endIndex = min(startIndex + 64 , endIndex);
        //return;

        for(uint i=startIndex; i < endIndex; ++i) 
        {
            uint pointIndex = CellPointIndices[i];
            if( pointIndex == DTid.x)
                continue;

            float3 otherPos = points[pointIndex].Position;
            float3 direction = position - otherPos;
            float distance = length(direction);
            if(distance <= 0.0001) {
                distance = 0.001;
            }

            if(distance > Threshold)
                continue;

            float force = (Threshold - distance) / Threshold;
            force = pow(force,2);

            sumForces += direction * force;
            count++;
        }

        if(count > 0) 
        {
            float acceleration = length(sumForces);
            float3 direction = sumForces / acceleration;
            

            //sumForces /= count;
            points[DTid.x].Position += direction * (Dispersion * 0.01) * clamp(acceleration, 0, ClampAccelleration);
            //points[DTid.x].w = min(count,3) * 1;
        }
    }
}