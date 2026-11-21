// Bake a signed distance field volume from a triangle mesh.
//
// Stage 1 (MeshToVoxel):  scatter sample points per triangle into an occupancy volume
// Stage 2 (ClassifyInside*): parity voting along the x/y/z axes builds an inside mask
// Stage 3 (Preprocess/JFA/Postprocess): jump flooding spreads nearest-surface voxel
//           coordinates and writes normalized signed distances.
//
// Ported from Fire-Aalt/com.firealt.mesh-to-sdf (Runtime/Resources/*.compute).
// Note: the voxel textures are ping-ponged through SRV reads, so no typed UAV
// loads are required beyond R32_FLOAT for the inside mask.
//
// All kernels share the Params cbuffer. KernelParam is used by MeshToVoxel
// (triangle count) and Jfa (sampling offset). All resources have unique
// registers so every kernel can be compiled from this single file.

struct PbrVertex
{
    float3 Position;
    float3 Normal;
    float3 Tangent;
    float3 Bitangent;
    float2 TexCoord;
    float2 TexCoord2;
    float Selected;
    float3 ColorRGB;
};

StructuredBuffer<PbrVertex> VertexBuffer : register(t0);
StructuredBuffer<uint3> FaceIndices : register(t1);
Texture3D<float4> VoxelRead : register(t2);      // nearest-surface seeds or occupancy (x)
Texture3D<float> InsideMaskRead : register(t3);
Texture3D<uint> ScratchRead : register(t4);      // scatter mode: flipped float distances
Texture3D<float> SdfRead : register(t5);         // smoothing: the baked distance volume

SamplerState MeshSdfLinear : register(s0);       // linear clamp, used by the smoothing passes

RWTexture3D<float4> VoxelWrite : register(u0);   // occupancy / seed coordinates
RWTexture3D<float> InsideMaskWrite : register(u1);
RWTexture3D<float> SdfWrite : register(u2);      // final normalized signed distance
RWTexture3D<uint> ScratchWrite : register(u3);   // scatter mode: flipped float distances
RWStructuredBuffer<float3> BoundsWrite : register(u4); // [0] = mesh min, [1] = mesh max (ComputeBounds)

cbuffer Params : register(b0)
{
    uint3 VoxelSize;
    uint KernelParam;               // MeshToVoxel: face count, Jfa: sampling offset
    float3 VoxelOrigin;             // world position of voxel (0,0,0)
    float VoxelScale;               // voxels per world unit
    float SampleDensity;            // MeshToVoxel: sample points per voxel^2 of triangle area
    float DistanceNormalization;    // divides voxel distances (volume resolution)
    int InsideVoteThreshold;
    int MaxSamples;                 // MeshToVoxel: upper bound of samples per triangle
    float ClearValue;               // written by ClearSdf
    float Signed;                   // 0 = keep distance unsigned (JumpFlood_Unsigned mode)
    float SliceZ;                   // z slice processed by ClosestPointBrute
    float Smoothing;                // 0..1 blend toward the local average
    float SmoothingRadius;          // smoothing reach in voxels
    float Invert;                   // 1 = flip inside/outside sign
    float BandRowStart;            // ClosestPointBrute: first row of this dispatch's band
    float Padding5;
}

// from https://beta.observablehq.com/@jrus/plastic-sequence
float2 plastic(float index)
{
    return float2(frac(0.7548776662466927 * index), frac(0.5698402909980532 * index));
}

// sample n on the triangle (origin, edgeA, edgeB) using a low discrepancy sequence
float3 triangleSample(int n, float3 origin, float3 edgeA, float3 edgeB)
{
    float2 s = plastic((float)n);
    s = s.x + s.y > 1.0 ? 1.0 - s : s;
    return origin + s.x * edgeA + s.y * edgeB;
}

[numthreads(8, 8, 8)]
void ClearVoxels(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    VoxelWrite[id] = float4(0, 0, 0, 0);
    InsideMaskWrite[id] = 0;
}

// Initialize the distance volume with a large positive distance while no mesh has been baked
[numthreads(8, 8, 8)]
void ClearSdf(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    SdfWrite[id] = ClearValue;
}

[numthreads(128, 1, 1)]
void MeshToVoxel(uint3 id : SV_DispatchThreadID)
{
    uint faceId = id.x;
    if (faceId >= KernelParam)
        return;

    uint3 face = FaceIndices[faceId];
    float3 a = (VertexBuffer[face.x].Position - VoxelOrigin) * VoxelScale;
    float3 b = (VertexBuffer[face.y].Position - VoxelOrigin) * VoxelScale;
    float3 c = (VertexBuffer[face.z].Position - VoxelOrigin) * VoxelScale;
    float3 ab = b - a;
    float3 ac = c - a;
    uint3 lastVoxelIdx = uint3(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu);

    // Scale sample count with the triangle area (in voxel units) so that even very
    // large / low-poly triangles produce a gap-free surface. Sparse sampling would
    // break the inside/outside parity voting later on.
    float area = 0.5 * length(cross(ab, ac));
    int sampleCount = (int)clamp(area * SampleDensity, 16.0, (float)MaxSamples);

    for (int i = 0; i < sampleCount; i++)
    {
        float3 pointOnTri = triangleSample(i, a, ab, ac);
        uint3 voxelIdx = uint3(floor(pointOnTri));
        if (!any(voxelIdx >= VoxelSize) && any(voxelIdx != lastVoxelIdx))
        {
            // The surface stage only needs a non-zero occupancy marker.
            VoxelWrite[voxelIdx] = float4(1, 0, 0, 1);
            lastVoxelIdx = voxelIdx;
        }
    }
}

void FillInsideSegmentX(uint y, uint z, int startX, int endX)
{
    for (int x = startX; x < endX; x++)
    {
        uint3 at = uint3((uint)x, y, z);
        InsideMaskWrite[at] = InsideMaskWrite[at] + 1.0;
    }
}

void FillInsideSegmentY(uint x, uint z, int startY, int endY)
{
    for (int y = startY; y < endY; y++)
    {
        uint3 at = uint3(x, (uint)y, z);
        InsideMaskWrite[at] = InsideMaskWrite[at] + 1.0;
    }
}

void FillInsideSegmentZ(uint x, uint y, int startZ, int endZ)
{
    for (int z = startZ; z < endZ; z++)
    {
        uint3 at = uint3(x, y, (uint)z);
        InsideMaskWrite[at] = InsideMaskWrite[at] + 1.0;
    }
}

[numthreads(1, 8, 8)]
void ClassifyInsideX(uint3 id : SV_DispatchThreadID)
{
    if (id.y >= VoxelSize.y || id.z >= VoxelSize.z)
        return;
    bool onSurfaceRun = false;
    int pendingInsideStart = -1;
    for (uint x = 0; x < VoxelSize.x; x++)
    {
        bool isSurface = VoxelRead.Load(int4(x, id.y, id.z, 0)).x > 0.5;
        if (isSurface)
        {
            if (!onSurfaceRun)
            {
                if (pendingInsideStart < 0)
                {
                    pendingInsideStart = (int)x + 1;
                }
                else
                {
                    FillInsideSegmentX(id.y, id.z, pendingInsideStart, (int)x);
                    pendingInsideStart = -1;
                }
                onSurfaceRun = true;
            }
        }
        else
        {
            onSurfaceRun = false;
        }
    }
}

[numthreads(8, 1, 8)]
void ClassifyInsideY(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= VoxelSize.x || id.z >= VoxelSize.z)
        return;
    bool onSurfaceRun = false;
    int pendingInsideStart = -1;
    for (uint y = 0; y < VoxelSize.y; y++)
    {
        bool isSurface = VoxelRead.Load(int4(id.x, y, id.z, 0)).x > 0.5;
        if (isSurface)
        {
            if (!onSurfaceRun)
            {
                if (pendingInsideStart < 0)
                {
                    pendingInsideStart = (int)y + 1;
                }
                else
                {
                    FillInsideSegmentY(id.x, id.z, pendingInsideStart, (int)y);
                    pendingInsideStart = -1;
                }
                onSurfaceRun = true;
            }
        }
        else
        {
            onSurfaceRun = false;
        }
    }
}

[numthreads(8, 8, 1)]
void ClassifyInsideZ(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= VoxelSize.x || id.y >= VoxelSize.y)
        return;
    bool onSurfaceRun = false;
    int pendingInsideStart = -1;
    for (uint z = 0; z < VoxelSize.z; z++)
    {
        bool isSurface = VoxelRead.Load(int4(id.x, id.y, z, 0)).x > 0.5;
        if (isSurface)
        {
            if (!onSurfaceRun)
            {
                if (pendingInsideStart < 0)
                {
                    pendingInsideStart = (int)z + 1;
                }
                else
                {
                    FillInsideSegmentZ(id.x, id.y, pendingInsideStart, (int)z);
                    pendingInsideStart = -1;
                }
                onSurfaceRun = true;
            }
        }
        else
        {
            onSurfaceRun = false;
        }
    }
}

// Copy occupancy into seed coordinates: surface voxels seed the flood fill.
[numthreads(8, 8, 8)]
void Preprocess(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    float isSurface = VoxelRead.Load(int4(id, 0)).x;
    VoxelWrite[id] = float4(id, isSurface > 0.5 ? 1.0 : 0.0);
}

void JfaIter(uint offset, uint3 id)
{
    float4 closest = VoxelRead.Load(int4(id, 0));
    float closestDistSq = 3.402823466e+38;
    int3 bounds = int3(VoxelSize);
    int intOffset = (int)offset;
    for (int i = -1; i <= 1; i++)
    {
        for (int j = -1; j <= 1; j++)
        {
            for (int k = -1; k <= 1; k++)
            {
                int3 at = int3(id) + int3(i, j, k) * intOffset;
                if (any(at < 0) || any(at >= bounds))
                    continue;
                float4 voxel = VoxelRead.Load(int4(at, 0));
                // not a seed / hasn't seen a seed
                if (voxel.w == 0.0)
                    continue;
                float3 delta = float3(id) - voxel.xyz;
                float voxelDistSq = dot(delta, delta);
                if (voxelDistSq < closestDistSq)
                {
                    closestDistSq = voxelDistSq;
                    closest = voxel;
                }
            }
        }
    }
    VoxelWrite[id] = closest;
}

[numthreads(8, 8, 8)]
void Jfa(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    JfaIter(KernelParam, id);
}

[numthreads(8, 8, 8)]
void Postprocess(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;

    // A voxel that never found a seed (the mesh has no surface inside the bake box, e.g. the
    // mesh moved out or AutoFit is off and the box is elsewhere) keeps w==0 and seedPos==id:
    // without this guard its distance computes as 0, which the raymarcher treats as a surface
    // hit at the first step - the whole box renders as one solid block.
    if (VoxelRead.Load(int4(id, 0)).w == 0.0)
    {
        SdfWrite[id] = ClearValue;
        return;
    }

    float3 seedPos = VoxelRead.Load(int4(id, 0)).xyz;
    float3 delta = seedPos - float3(id);
    float dist = sqrt(dot(delta, delta)) / DistanceNormalization;
    bool inside = Signed > 0.5 && InsideMaskRead.Load(int4(id, 0)) >= InsideVoteThreshold;
    // Inverting is only meaningful for a signed field; in unsigned mode `inside` is
    // always false, so flipping it would turn the whole volume negative.
    if (Invert > 0.5 && Signed > 0.5)
        inside = !inside;
    SdfWrite[id] = inside ? -dist : dist;
}

// ---------------------------------------------------------------- exact mode

// Squared distance from p to triangle (a,b,c), in voxel units.
float PointTriangleDistSq(float3 p, float3 a, float3 b, float3 c)
{
    float3 ab = b - a;
    float3 ac = c - a;
    float3 ap = p - a;
    float d1 = dot(ab, ap);
    float d2 = dot(ac, ap);
    if (d1 <= 0 && d2 <= 0)
        return dot(ap, ap);
    float3 bp = p - b;
    float d3 = dot(ab, bp);
    float d4 = dot(ac, bp);
    if (d3 >= 0 && d4 <= d3)
        return dot(bp, bp);
    float vc = d1 * d4 - d3 * d2;
    if (vc <= 0 && d1 >= 0 && d3 <= 0)
    {
        float v = d1 / max(d1 - d3, 1e-20); // max() keeps degenerate triangles from producing 0/0
        float3 q = a + ab * v - p;
        return dot(q, q);
    }
    float3 cp = p - c;
    float d5 = dot(ab, cp);
    float d6 = dot(ac, cp);
    if (d6 >= 0 && d5 <= d6)
        return dot(cp, cp);
    float vb = d5 * d2 - d1 * d6;
    if (vb <= 0 && d2 >= 0 && d6 <= 0)
    {
        float w = d2 / max(d2 - d6, 1e-20);
        float3 q = a + ac * w - p;
        return dot(q, q);
    }
    float va = d3 * d6 - d5 * d4;
    if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
    {
        float w = (d4 - d3) / max((d4 - d3) + (d5 - d6), 1e-20);
        float3 q = b + (c - b) * w - p;
        return dot(q, q);
    }
    float denom = 1.0 / max(va + vb + vc, 1e-20);
    float v = vb * denom;
    float w = vc * denom;
    float3 q = a + ab * v + ac * w - p;
    return dot(q, q);
}

// Exact distance from every voxel center to the nearest triangle (brute force).
// Dispatched one z slice at a time so the OS can preempt between dispatches.
[numthreads(8, 8, 1)]
void ClosestPointBrute(uint3 tid : SV_DispatchThreadID)
{
    // The slice is split into row bands on the CPU so one dispatch can never run past
    // the OS GPU watchdog; BandRowStart is this dispatch's first row.
    uint3 id = uint3(tid.x, (uint)BandRowStart + tid.y, (uint)SliceZ);
    if (any(id >= VoxelSize))
        return;

    float3 p = float3(id) + 0.5;
    float bestDistSq = 3.402823466e+38;
    for (uint f = 0; f < KernelParam; f++)
    {
        uint3 face = FaceIndices[f];
        float3 a = (VertexBuffer[face.x].Position - VoxelOrigin) * VoxelScale;
        float3 b = (VertexBuffer[face.y].Position - VoxelOrigin) * VoxelScale;
        float3 c = (VertexBuffer[face.z].Position - VoxelOrigin) * VoxelScale;
        float distSq = PointTriangleDistSq(p, a, b, c);
        bestDistSq = min(bestDistSq, distSq);
    }

    VoxelWrite[id] = float4(sqrt(bestDistSq) / DistanceNormalization, 0, 0, 0);
}

// Applies the inside mask sign to an unsigned distance volume (exact mode).
[numthreads(8, 8, 8)]
void ApplyInsideSign(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    float dist = VoxelRead.Load(int4(id, 0)).x;
    bool inside = Signed > 0.5 && InsideMaskRead.Load(int4(id, 0)) >= InsideVoteThreshold;
    // Inverting is only meaningful for a signed field; in unsigned mode `inside` is
    // always false, so flipping it would turn the whole volume negative.
    if (Invert > 0.5 && Signed > 0.5)
        inside = !inside;
    SdfWrite[id] = inside ? -dist : dist;
}

// --------------------------------------- exact scatter mode (AMD TressFX style)
// This is the method used by vvvv's VL.Fuse.DomainExtensions MeshToSDF:
// every triangle writes the exact UNSIGNED distance into the grid cells around its
// bounding box; an atomic minimum keeps the closest surface. Distances are exact
// within the margin shell around the mesh; far cells keep the initial value.
// FinalizeSdfScatter then applies the inside/outside sign from the parity-vote mask -
// a per-triangle normal sign would be invalid for concave surfaces.

// order-preserving float -> uint mapping (exact for the unsigned distances we
// scatter) so InterlockedMin finds the smallest distance
uint FloatFlipDist(float f)
{
    uint x = asuint(f);
    return (x << 1) | (x >> 31);
}

float IFloatFlipDist(uint x)
{
    return asfloat((x >> 1) | (x << 31));
}

[numthreads(8, 8, 8)]
void InitSdfScatter(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    ScratchWrite[id] = FloatFlipDist(ClearValue);
}

[numthreads(64, 1, 1)]
void BakeSDFScatter(uint3 tid : SV_DispatchThreadID)
{
    const int margin = 15;

    uint faceId = tid.x;
    if (faceId >= KernelParam)
        return;

    uint3 face = FaceIndices[faceId];
    float3 a = (VertexBuffer[face.x].Position - VoxelOrigin) * VoxelScale;
    float3 b = (VertexBuffer[face.y].Position - VoxelOrigin) * VoxelScale;
    float3 c = (VertexBuffer[face.z].Position - VoxelOrigin) * VoxelScale;

    int3 gridMin = max(int3(0, 0, 0), (int3)floor(min(a, min(b, c))) - margin);
    int3 gridMax = min((int3)VoxelSize - 1, (int3)ceil(max(a, max(b, c))) + margin);

    for (int z = gridMin.z; z <= gridMax.z; z++)
    {
        for (int y = gridMin.y; y <= gridMax.y; y++)
        {
            for (int x = gridMin.x; x <= gridMax.x; x++)
            {
                float3 p = float3(x, y, z) + 0.5;
                float dist = sqrt(PointTriangleDistSq(p, a, b, c)) / DistanceNormalization;
                // Unsigned only: the per-triangle normal is not a valid inside test
                // for concave meshes. FinalizeSdfScatter applies the parity-vote sign.
                InterlockedMin(ScratchWrite[uint3(x, y, z)], FloatFlipDist(dist));
            }
        }
    }
}

[numthreads(8, 8, 8)]
void FinalizeSdfScatter(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    float dist = abs(IFloatFlipDist(ScratchRead.Load(int4(id, 0))));
    bool inside = Signed > 0.5 && InsideMaskRead.Load(int4(id, 0)) >= InsideVoteThreshold;
    // Inverting is only meaningful for a signed field; in unsigned mode `inside` is
    // always false, so flipping it would turn the whole volume negative.
    if (Invert > 0.5 && Signed > 0.5)
        inside = !inside;
    SdfWrite[id] = inside ? -dist : dist;
}

// ------------------------------------------------------------- bounds fit

// Reduce the mesh bounding box (mesh space, before any voxel transform) into
// BoundsWrite[0..1]. Dispatched as a single 64-thread group; threads stride over
// the faces, then a tree reduction collects the group result.
groupshared float3 gsMeshMin[64];
groupshared float3 gsMeshMax[64];

[numthreads(64, 1, 1)]
void ComputeBounds(uint3 tid : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex)
{
    const uint groupSize = 64;

    float3 tMin = float3(3.4e38, 3.4e38, 3.4e38);
    float3 tMax = -tMin;
    for (uint f = tid.x; f < KernelParam; f += groupSize)
    {
        uint3 face = FaceIndices[f];
        float3 a = VertexBuffer[face.x].Position;
        float3 b = VertexBuffer[face.y].Position;
        float3 c = VertexBuffer[face.z].Position;
        tMin = min(tMin, min(a, min(b, c)));
        tMax = max(tMax, max(a, max(b, c)));
    }

    gsMeshMin[groupIndex] = tMin;
    gsMeshMax[groupIndex] = tMax;
    GroupMemoryBarrierWithGroupSync();

    [unroll]
    for (uint stride = groupSize / 2; stride > 0; stride >>= 1)
    {
        if (groupIndex < stride)
        {
            gsMeshMin[groupIndex] = min(gsMeshMin[groupIndex], gsMeshMin[groupIndex + stride]);
            gsMeshMax[groupIndex] = max(gsMeshMax[groupIndex], gsMeshMax[groupIndex + stride]);
        }
        GroupMemoryBarrierWithGroupSync();
    }

    if (groupIndex == 0)
    {
        BoundsWrite[0] = gsMeshMin[0];
        BoundsWrite[1] = gsMeshMax[0];
    }
}

// ------------------------------------------------------------- smoothing

// Average of the 27 neighbors (scaled by SmoothingRadius), trilinearly sampled
// so the radius can be fractional. Border behavior: clamp to the volume edge.
float MeshSdfAverage(uint3 id)
{
    float sum = 0;
    [unroll]
    for (int z = -1; z <= 1; z++)
    {
        [unroll]
        for (int y = -1; y <= 1; y++)
        {
            [unroll]
            for (int x = -1; x <= 1; x++)
            {
                float3 uvw = (float3(id) + 0.5 + float3(x, y, z) * SmoothingRadius) / float3(VoxelSize);
                sum += VoxelRead.SampleLevel(MeshSdfLinear, uvw, 0).x;
            }
        }
    }
    return sum / 27.0;
}

// First smoothing pass reads the freshly baked distance volume
[numthreads(8, 8, 8)]
void SmoothFirstPass(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    float center = SdfRead.Load(int4(id, 0));
    float avg = MeshSdfAverage(id);
    VoxelWrite[id] = float4(lerp(center, avg, Smoothing), 0, 0, 1);
}

// Second smoothing pass ping-pongs through the voxel textures
[numthreads(8, 8, 8)]
void SmoothPass(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    float center = VoxelRead.Load(int4(id, 0)).x;
    float avg = MeshSdfAverage(id);
    VoxelWrite[id] = float4(lerp(center, avg, Smoothing), 0, 0, 1);
}

// Copies the smoothed result back into the distance volume
[numthreads(8, 8, 8)]
void SmoothCopyBack(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= VoxelSize))
        return;
    SdfWrite[id] = VoxelRead.Load(int4(id, 0)).x;
}
