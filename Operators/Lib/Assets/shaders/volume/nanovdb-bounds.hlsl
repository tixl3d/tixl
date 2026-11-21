// NanoVDB node bounds as line points.
//
// Debug view of the sparse structure: one wireframe box per node of the chosen level, emitted as
// separator + two points per edge so the output can go straight into DrawLines. Use it to check
// that bounds, transforms and node counts line up with the volume before trusting a raymarch.
//
// Bindings:
//   t0  the NanoVDB grid (LoadNanoVdb / VolumeToNanoVdb "Buffer")
//   u0  the output Point buffer
//   u1  a single-uint counter, owned by the dispatcher

#include "shared/point.hlsl"
#include "volume/nanovdb-common.hlsl"

cbuffer Params : register(b0)
{
    float4 Color;
    uint   Level;        // 0 = leaves, 1 = lower nodes, 2 = upper nodes
    uint   MaxNodes;     // dispatch bound; past the real count the shader returns early
    uint   Capacity;     // Point slots in the output buffer
    uint   _padding;
};

StructuredBuffer<uint>    Grid    : register(t0);
RWStructuredBuffer<Point> Points  : register(u0);
RWStructuredBuffer<uint>  Counter : register(u1);

static const uint EdgesPerBox = 12;
static const uint SlotsPerBox = EdgesPerBox * 3;   // separator + 2 endpoints per edge

// Corner bit order matches (x,y,z) = (bit0, bit1, bit2).
static const uint EdgeCorners[24] =
{
    0, 1,  1, 3,  3, 2,  2, 0,   // bottom face
    4, 5,  5, 7,  7, 6,  6, 4,   // top face
    0, 4,  1, 5,  2, 6,  3, 7,   // verticals
};

Point MakePoint(in VdbGridInfo info, float3 position, float4 color)
{
    Point p;
    p.Position = position;
    p.FX1 = 0;
    p.Rotation = float4(0, 0, 0, 1);
    p.Color = color;
    p.Scale = float3(info.voxelSize, info.voxelSize, info.voxelSize) * 0.5;
    p.FX2 = 1;
    return p;
}

[numthreads(64, 1, 1)]
void main(uint3 groupId : SV_GroupID, uint tid : SV_GroupIndex)
{
    uint nodeIndex = groupId.x * 64u + tid;
    if (nodeIndex >= MaxNodes || nodeIndex >= VdbNodeCount(Grid, Level))
        return;

    uint slotBase;
    InterlockedAdd(Counter[0], SlotsPerBox, slotBase);
    if (slotBase + SlotsPerBox > Capacity)
        return;

    VdbGridInfo info = VdbReadGridInfo(Grid);
    uint nodeAddress = VdbNodeSection(Grid, Level) + nodeIndex * VdbNodeStride(Level);
    int3 bboxMin = VdbNodeBBoxMin(Grid, nodeAddress);
    uint dim = VdbNodeDim(Level);

    uint write = slotBase;
    for (uint edge = 0; edge < EdgesPerBox; edge++)
    {
        int3 a = bboxMin + int3(EdgeCorners[edge * 2 + 0] & 1, (EdgeCorners[edge * 2 + 0] >> 1) & 1, (EdgeCorners[edge * 2 + 0] >> 2) & 1) * dim;
        int3 b = bboxMin + int3(EdgeCorners[edge * 2 + 1] & 1, (EdgeCorners[edge * 2 + 1] >> 1) & 1, (EdgeCorners[edge * 2 + 1] >> 2) & 1) * dim;

        Point separator = MakePoint(info, float3(0, 0, 0), float4(0, 0, 0, 0));
        separator.Scale = float3(asfloat(0x7FC00000), 0, 0);   // NaN -> IsSeparator()
        Points[write++] = separator;
        Points[write++] = MakePoint(info, VdbIndexCornerWorldPosition(info, a), Color);
        Points[write++] = MakePoint(info, VdbIndexCornerWorldPosition(info, b), Color);
    }
}
