#include "shared/point.hlsl"

RWStructuredBuffer<Point> points :register(u0); 

cbuffer Params : register(b0)
{
    float3 Center; 
    float CellSize;
}

#include "points/spatial-hash-map/spatial-hash-map-lookup.hlsl"


[numthreads(1, 1, 1 )]
void FlagPoints(uint3 DTid : SV_DispatchThreadID, uint GI: SV_GroupIndex)
{
    uint pointCount, stride;
    points.GetDimensions(pointCount, stride);
        
    //points[DTid.x].w = 10;

    if(DTid.x >= pointCount)
        return; // out of bounds

    if(DTid.x != 0) 
        return;

    float3 position = Center + 0.0;

    int startIndex, endIndex;
    if(GridFind(position, startIndex, endIndex)) 
    {
        for(uint i=startIndex; i < endIndex; ++i) 
        {
            uint pointIndex = CellPointIndices[i];
            points[pointIndex].w = 5;
        }
    } 
} 