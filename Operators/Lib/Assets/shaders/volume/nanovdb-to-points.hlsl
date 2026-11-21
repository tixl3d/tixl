// NanoVDB voxel -> TiXL points.
//
// One thread group per leaf node: the group index picks the leaf, the thread index picks the
// voxel inside its 8^3 block. Active voxels above a threshold append one Point each; a second
// dispatch of the same shader (Pass = 1) fills the unused tail with separators so downstream
// point operators stop at the real count.
//
// Bindings:
//   t0  the NanoVDB grid (LoadNanoVdb / VolumeToNanoVdb "Buffer")
//   t1  a single-uint counter, zeroed by the dispatcher before the append pass
//   u0  the output Point buffer

#include "shared/point.hlsl"
#include "volume/nanovdb-common.hlsl"

cbuffer Params : register(b0)
{
    float Threshold;      // voxels with a value below this are skipped
    uint  MaxPoints;      // capacity of the output buffer
    uint  Pass;           // 0 = append, 1 = fill the unused tail with separators
    uint  MaxLeaves;      // dispatch bound; leaves past the real count are skipped by the shader
};

StructuredBuffer<uint>    Grid    : register(t0);
RWStructuredBuffer<uint>  Counter : register(u1);   // one uint: how many points pass 0 wrote
RWStructuredBuffer<Point> Points  : register(u0);

[numthreads(512, 1, 1)]
void main(uint3 groupId : SV_GroupID, uint slot : SV_GroupIndex)
{
    if (Pass == 1)
    {
        // Separator: NaN Scale.x is TiXL's "hidden point" mark. The dispatcher sizes this pass
        // from MaxPoints, so one thread maps to one slot.
        uint i = groupId.x * 512u + slot;
        if (i >= MaxPoints || i < Counter[0])
            return;

        Point sep;
        sep.Position = float3(0, 0, 0);
        sep.FX1 = 0;
        sep.Rotation = float4(0, 0, 0, 1);
        sep.Color = float4(0, 0, 0, 0);
        sep.Scale = float3(asfloat(0x7FC00000), 0, 0);   // NaN -> IsSeparator()
        sep.FX2 = 0;
        Points[i] = sep;
        return;
    }

    uint leafIndex = groupId.x;
    if (leafIndex >= MaxLeaves || leafIndex >= VdbLeafCount(Grid))
        return;

    uint leafAddress = VdbLeafSection(Grid) + leafIndex * VdbLeafStride();
    if (!VdbLeafIsActive(Grid, leafAddress, slot))
        return;

    float value = VdbLeafValue(Grid, leafAddress, slot);
    if (!(value >= Threshold))
        return;

    uint index;
    InterlockedAdd(Counter[0], 1u, index);
    if (index >= MaxPoints)
        return;

    VdbGridInfo info = VdbReadGridInfo(Grid);
    int3 ijk = VdbLeafVoxelCoord(VdbLeafBBoxMin(Grid, leafAddress), slot);

    Point p;
    p.Position = VdbVoxelWorldPosition(info, ijk);
    p.FX1 = value;                        // the voxel value, for size or colour downstream
    p.Rotation = float4(0, 0, 0, 1);
    p.Color = float4(1, 1, 1, 1);
    p.Scale = float3(info.voxelSize, info.voxelSize, info.voxelSize);
    p.FX2 = 1;
    Points[index] = p;
}
