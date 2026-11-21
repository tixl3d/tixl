// Shared NanoVDB traversal for TiXL compute shaders.
//
// Layout constants and the tree walk are the ones NanoVdbVolume.hlsl has been using; they follow
// nanovdb/NanoVDB.h (NanoVDB 32.11) as written by NanoVdbBuffer and the reference nanovdb_convert.
// Keep them in sync with Core/DataTypes/Volume/Tools/NanoVdbBuffer.cs if either side changes.
//
// Generic over the value type where it matters: the offsets below describe a float grid. A vec3
// grid has 12 extra bytes of node statistics in root/upper/lower headers, so its table offsets
// differ - read those grids with their own constants, not with these.

#ifndef TIXL_NANOVDB_COMMON
#define TIXL_NANOVDB_COMMON

static const uint GridDataBytes      = 672;   // GridData: magic..+file fields, map, bbox, name
static const uint MapMatfBytes       = 296;   // Map: matf[9], invmatf[9], vecf[3] at 296+72
static const uint MapVecfBytes       = 296 + 72;
static const uint RootHeaderBytes    = 64;    // bbox + tableSize + background/min/max + avg/dev
static const uint RootTileBytes      = 32;    // key, child, state, value
static const uint UpperChildMaskBytes = 24 + 8 + 4096;
static const uint UpperTableBytes    = 8256;
static const uint LowerChildMaskBytes = 24 + 8 + 512;
static const uint LowerTableBytes    = 1088;
static const uint LeafValuesBytes    = 96;    // bbox 12 + dif 3 + flags 1 + mask 64 + stats 16
static const uint LeafValueMaskBytes = 16;    // offset of the 64-byte value mask word array
static const uint LeafValueCount     = 512;
static const uint LeafValueStride    = 4;     // bytes per value (float grid)

struct VdbGridInfo
{
    uint rootOffset;
    uint tileCount;
    float background;
    float voxelSize;
    float3 translation;
    int3 indexMin;
    int3 indexMax;
    float3 worldMin;
    float3 worldMax;
};

VdbGridInfo VdbReadGridInfo(StructuredBuffer<uint> g)
{
    VdbGridInfo info;
    info.rootOffset = GridDataBytes + g[(GridDataBytes + 24) >> 2];
    uint root = info.rootOffset >> 2;
    info.indexMin = int3(g[root], g[root + 1], g[root + 2]);
    info.indexMax = int3(g[root + 3], g[root + 4], g[root + 5]);
    info.tileCount = g[root + 6];
    info.background = asfloat(g[root + 7]);
    info.voxelSize = asfloat(g[MapMatfBytes >> 2]);
    uint vec = MapVecfBytes >> 2;
    info.translation = float3(asfloat(g[vec]), asfloat(g[vec + 1]), asfloat(g[vec + 2]));

    // Outside this box every sample is the background, so callers can reject early.
    info.worldMin = info.translation + (info.indexMin - 1) * info.voxelSize;
    info.worldMax = info.translation + (info.indexMax + 1) * info.voxelSize;
    return info;
}

/// Tree section offsets are relative to the start of the grid buffer, stored tree-relative.
uint VdbLeafSection(StructuredBuffer<uint> g)
{
    return GridDataBytes + g[GridDataBytes >> 2];
}

uint VdbLeafCount(StructuredBuffer<uint> g)
{
    return g[(GridDataBytes + 32) >> 2];
}

/// Byte stride of one leaf node: its header, its 512 value mask bits, its stats and its values.
uint VdbLeafStride()
{
    return LeafValuesBytes + LeafValueCount * LeafValueStride;
}

// ---------- internal nodes, for diagnostics that walk the tree itself ----------

static const uint LowerNodeStride = LowerTableBytes + 4096 * 8;     // 16^3 slots of child+value
static const uint UpperNodeStride = UpperTableBytes + 32768 * 8;    // 32^3 slots of child+value
static const uint LowerNodeDim    = 128;                            // extent of one lower node
static const uint UpperNodeDim    = 4096;                           // extent of one upper node

/// Byte offset of the section holding the node level: 0 leaf, 1 lower, 2 upper.
uint VdbNodeSection(StructuredBuffer<uint> g, uint level)
{
    uint pointer = level == 0 ? GridDataBytes : (level == 1 ? GridDataBytes + 8 : GridDataBytes + 16);
    return GridDataBytes + g[pointer >> 2];
}

/// How many nodes the level holds, as stored in the tree header.
uint VdbNodeCount(StructuredBuffer<uint> g, uint level)
{
    uint pointer = level == 0 ? (GridDataBytes + 32) : (level == 1 ? (GridDataBytes + 36) : (GridDataBytes + 40));
    return g[pointer >> 2];
}

uint VdbNodeStride(uint level)
{
    return level == 0 ? VdbLeafStride() : (level == 1 ? LowerNodeStride : UpperNodeStride);
}

uint VdbNodeDim(uint level)
{
    return level == 0 ? 8 : (level == 1 ? LowerNodeDim : UpperNodeDim);
}

/// Every node begins with the same bbox triple, so one reader covers all three levels.
int3 VdbNodeBBoxMin(StructuredBuffer<uint> g, uint nodeAddress)
{
    uint b = nodeAddress >> 2;
    return int3(g[b], g[b + 1], g[b + 2]);
}

/// World position of an index-space corner, without the half-voxel offset used for samples.
float3 VdbIndexCornerWorldPosition(in VdbGridInfo info, int3 indexCorner)
{
    return info.translation + (float3)indexCorner * info.voxelSize;
}

// ---------- world-space sampling, for fields and other per-sample callers ----------
//
// NanoVdbVolume.hlsl carries its own copy of this walk, tuned for a raymarcher that builds the
// grid info once per pixel. It is deliberately not switched over to these helpers here, because
// that shader cannot be run by the test suite; when it is next touched, it should include this
// file and drop its duplicates.

/// Walks root -> upper -> lower and returns the leaf byte offset, or 0 when the voxel lies in a
/// constant tile, whose value is returned through tileValue.
uint VdbFindLeaf(StructuredBuffer<uint> g, in VdbGridInfo info, int3 ijk, out float tileValue)
{
    tileValue = info.background;

    uint3 key = uint3(ijk >> 12) & 0x1FFFFFu;
    uint keyLow = key.z | ((key.y & 0x7FFu) << 21);
    uint keyHigh = (key.y >> 11) | (key.x << 10);

    uint upper = 0;
    uint firstTile = (info.rootOffset + RootHeaderBytes) >> 2;
    [loop]
    for (uint tileIndex = 0; tileIndex < info.tileCount; tileIndex++)
    {
        uint tile = firstTile + tileIndex * (RootTileBytes >> 2);
        if (g[tile] != keyLow || g[tile + 1] != keyHigh)
            continue;

        uint child = g[tile + 2];
        if (child == 0)
        {
            tileValue = asfloat(g[tile + 5]);
            return 0;
        }

        upper = info.rootOffset + child;
        break;
    }

    if (upper == 0)
        return 0;

    uint3 u = uint3(ijk & 4095) >> 7;
    uint upperSlot = (u.x << 10) | (u.y << 5) | u.z;
    uint upperWord = g[(upper + UpperChildMaskBytes) / 4 + (upperSlot >> 5)];
    uint upperEntry = (upper + UpperTableBytes) / 4 + upperSlot * 2;
    if ((upperWord & (1u << (upperSlot & 31))) == 0)
    {
        tileValue = asfloat(g[upperEntry]);
        return 0;
    }

    uint lower = upper + g[upperEntry];
    uint3 l = uint3(ijk & 127) >> 3;
    uint lowerSlot = (l.x << 8) | (l.y << 4) | l.z;
    uint lowerWord = g[(lower + LowerChildMaskBytes) / 4 + (lowerSlot >> 5)];
    uint lowerEntry = (lower + LowerTableBytes) / 4 + lowerSlot * 2;
    if ((lowerWord & (1u << (lowerSlot & 31))) == 0)
    {
        tileValue = asfloat(g[lowerEntry]);
        return 0;
    }

    return lower + g[lowerEntry];
}

/// Word index of a voxel inside its leaf; leaf offsets run x * 64 + y * 8 + z.
uint VdbLeafValueIndex(uint leaf, int3 ijk)
{
    uint3 v = uint3(ijk & 7);
    return (leaf + LeafValuesBytes) / 4 + ((v.x << 6) | (v.y << 3) | v.z);
}

float VdbLookup(StructuredBuffer<uint> g, in VdbGridInfo info, int3 ijk)
{
    float tileValue;
    uint leaf = VdbFindLeaf(g, info, ijk, tileValue);
    return leaf == 0 ? tileValue : asfloat(g[VdbLeafValueIndex(leaf, ijk)]);
}

/// The last leaf a caller touched. Neighbouring samples nearly always land in the same 8^3 block,
/// so caching it skips the whole tree walk. One per thread, reused across samples.
struct VdbLeafCache
{
    int3 Block;
    uint Leaf;
    float TileValue;
};

VdbLeafCache VdbNewLeafCache()
{
    VdbLeafCache cache;
    cache.Block = int3(0x7FFFFFFF, 0, 0);
    cache.Leaf = 0;
    cache.TileValue = 0;
    return cache;
}

/// Trilinear sample in world space; voxel i is sampled at translation + i * voxel size.
float VdbSampleTrilinear(StructuredBuffer<uint> g, in VdbGridInfo info, float3 worldPos, inout VdbLeafCache cache)
{
    float3 q = (worldPos - info.translation) / info.voxelSize;
    int3 i0 = (int3)floor(q);
    if (any(i0 < info.indexMin - 1) || any(i0 > info.indexMax))
        return info.background;

    float3 f = q - i0;

    // All eight corners inside one leaf or one constant tile: at most one tree walk.
    if (all((i0 & 7) < 7))
    {
        int3 block = i0 & ~7;
        if (any(block != cache.Block))
        {
            cache.Leaf = VdbFindLeaf(g, info, i0, cache.TileValue);
            cache.Block = block;
        }

        if (cache.Leaf == 0)
            return cache.TileValue;

        uint corner = VdbLeafValueIndex(cache.Leaf, i0);
        float c000 = asfloat(g[corner]);
        float c001 = asfloat(g[corner + 1]);
        float c010 = asfloat(g[corner + 8]);
        float c011 = asfloat(g[corner + 9]);
        float c100 = asfloat(g[corner + 64]);
        float c101 = asfloat(g[corner + 65]);
        float c110 = asfloat(g[corner + 72]);
        float c111 = asfloat(g[corner + 73]);
        return lerp(lerp(lerp(c000, c100, f.x), lerp(c010, c110, f.x), f.y),
                    lerp(lerp(c001, c101, f.x), lerp(c011, c111, f.x), f.y), f.z);
    }

    // The cell straddles leaves: weighted corners, without local arrays (they end up in slow memory).
    float sum = 0;
    [loop]
    for (uint k = 0; k < 8; k++)
    {
        int3 offset = int3(k >> 2, (k >> 1) & 1, k & 1);
        float3 axisWeight = lerp(1.0 - f, f, (float3)offset);
        sum += axisWeight.x * axisWeight.y * axisWeight.z * VdbLookup(g, info, i0 + offset);
    }

    return sum;
}

/// True when voxel `slot` (0..511, order (x<<6)|(y<<3)|z) is active inside this leaf.
bool VdbLeafIsActive(StructuredBuffer<uint> g, uint leafAddress, uint slot)
{
    uint maskBase = (leafAddress + LeafValueMaskBytes) >> 2;
    uint word = g[maskBase + (slot >> 5)];
    return (word & (1u << (slot & 31))) != 0;
}

float VdbLeafValue(StructuredBuffer<uint> g, uint leafAddress, uint slot)
{
    return asfloat(g[((leafAddress + LeafValuesBytes) >> 2) + slot]);
}

int3 VdbLeafBBoxMin(StructuredBuffer<uint> g, uint leafAddress)
{
    uint b = leafAddress >> 2;
    return int3(g[b], g[b + 1], g[b + 2]);
}

/// Index-space coordinate of a voxel inside its leaf, in NanoVDB's value order.
int3 VdbLeafVoxelCoord(int3 bboxMin, uint slot)
{
    return bboxMin + int3((slot >> 6) & 7, (slot >> 3) & 7, slot & 7);
}

/// World position of a voxel centre, for a grid whose map is translation + uniform voxel size.
float3 VdbVoxelWorldPosition(in VdbGridInfo info, int3 ijk)
{
    return info.translation + ((float3)ijk + 0.5) * info.voxelSize;
}

#endif // TIXL_NANOVDB_COMMON
