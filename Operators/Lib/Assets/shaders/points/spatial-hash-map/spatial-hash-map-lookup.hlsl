StructuredBuffer<uint> CellPointIndices :register(t0);         // IndexToPointBuffer
StructuredBuffer<uint2> PointCellIndices :register(t1);    // CellIndicesBuffer -> PointCellIndices
StructuredBuffer<uint> HashGridCells :register(t2);     // HashGridBuffer -> HashGridCells
StructuredBuffer<uint> CellPointCounts :register(t3);    // CountBuffer -> CellPointCounts
StructuredBuffer<uint> CellRangeIndices :register(t4);    // RangeIndexBuffer -> CellRangeIndices

#include "shared/hash-functions.hlsl"
#include "points/spatial-hash-map/hash-map-settings.hlsl" 

bool ParticleGridFind(in float3 position, out uint2 entry)
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
        const uint entryValue = HashGridCells[i];
        if(entryValue == hashValue)
            break;  // found existing entry
        if(entryValue == 0)
            i = cellEnd;
    }
    if(i >= cellEnd)
        return false;
    entry.x = CellRangeIndices[i];
    entry.y = CellPointCounts[i] + entry.x;
    return true;
}

bool GridFind(in float3 position, out uint startIndex, out uint endIndex)
{
    position+=100 * CellSize;
    uint i;
    int3 cell = int3(position / CellSize);
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
    endIndex = CellPointCounts[i] + startIndex;
    return true;
}