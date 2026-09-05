// For more details on how this works see https://www.figma.com/file/wBNGUlaACjaCDOTdeBvBvR/ComputeShader-Ideas?node-id=8%3A0
// This code is derived after Guillaume Boissé 

#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/hash-functions.hlsl"
#include "points/spatial-hash-map/hash-map-settings.hlsl" 

StructuredBuffer<Point> _points :register(t0); 

RWStructuredBuffer<uint> CellPointIndices :register(u0);   // particleGridBuffer -> IndexToPointBuffer -> CellPointIndices
RWStructuredBuffer<uint2> PointCellIndices :register(u1);  // particleGridCellBuffer -> PointCellIndices
RWStructuredBuffer<uint> HashGridCells :register(u2);      // particleGridHashBuffer -> HashGridCells
RWStructuredBuffer<uint> CellPointCounts :register(u3);    // particleGridCountBuffer -> CellPointCounts
RWStructuredBuffer<uint> CellRangeIndices :register(u4);   // particleGridIndexBuffer -> CellRangeIndices

cbuffer Params : register(b0)
{
    float CellSize;
}

#define THREADS_PER_GROUP 256
 


bool ParticleGridInsert(in uint index, in float3 position)
{
    uint i;
    position+=100*CellSize;
    int3 cell = int3(position / CellSize);
    uint cellIndex = (pcg(cell.x + pcg(cell.y + pcg(cell.z))) % ParticleGridCellCount);
    uint hashValue = max(xxhash(cell.x + xxhash(cell.y + xxhash(cell.z))), 1);
    uint cellBegin = cellIndex * ParticleGridEntryCount;
    uint cellEnd = cellBegin + ParticleGridEntryCount;
    for(i = cellBegin; i < cellEnd; ++i)
    {
        uint entryValue;
        InterlockedCompareExchange(HashGridCells[i], 0, hashValue, entryValue);
        if(entryValue == 0 || entryValue == hashValue)
            break;  // found an available entry
    }
    if(i >= cellEnd)
        return false;   // out of memory

    //const uint particleOffset = atomicAdd(particleGridCountBuffer[i], 1);

    uint particleOffset = 0;        
    InterlockedAdd(CellPointCounts[i], 1, particleOffset);
    

    PointCellIndices[index] = uint2(i, particleOffset);
    return true;
}



//----------------------------------------------------------------------

[numthreads( THREADS_PER_GROUP, 1, 1 )]
void ClearParticleGrid(uint DTid : SV_DispatchThreadID, uint _GI: SV_GroupIndex)
{
    HashGridCells[DTid.x] = 0;
    CellPointCounts[DTid.x] = 0;
}



[numthreads( THREADS_PER_GROUP, 1, 1 )]
void CountParticlesPerCell(uint DTid : SV_DispatchThreadID, uint _GI: SV_GroupIndex)
{
    uint pointCount, stride;
    _points.GetDimensions(pointCount, stride);
    
    if(DTid.x >= pointCount)
        return; // out of bounds

    //const uint particleIndex = aliveIndexBuffer[DTid.x];
    const float3 position = _points[DTid.x].Position;

    if(!ParticleGridInsert(DTid.x, position))
        PointCellIndices[DTid.x] = uint2(uint(-1), 0);
}

[numthreads( THREADS_PER_GROUP, 1, 1 )]
void ScatterParticlesInCells(uint DTid : SV_DispatchThreadID, uint _GI: SV_GroupIndex)
{
    uint pointCount, stride;
    _points.GetDimensions(pointCount, stride);
        
    if(DTid.x >= pointCount)
        return; // out of bounds

    const uint2 gridCell = PointCellIndices[DTid.x];
    const uint cellIndex = gridCell.x;
    const uint gridEntryIndex =  gridCell.y;

    if(cellIndex == uint(-1))
        return; // out of memory

    //const uint particleIndex = aliveIndexBuffer[DTid.x];
    
    const uint rangeStartIndex = CellRangeIndices[cellIndex];
    const uint rangeLength = CellPointCounts[cellIndex];
    const uint particleOffset =  rangeStartIndex + gridEntryIndex;

    CellPointIndices[particleOffset] = DTid.x;
} 