#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

// Ring orientation pre-pass for DrawTubes: a rotation-minimizing frame along every line, so the tube neither twists
// nor depends on the point rotations. Each line's first point seeds the frame from its rotation's Y axis.

StructuredBuffer<Point> Points : register(t0);

#include "points/draw/DrawTubesCenterline.hlsl"

RWStructuredBuffer<RingFrame> OutRingFrames : register(u0);

float3 SeedNormal(Point linePoint, float3 tangent)
{
    float4 rotation = linePoint.Rotation;
    bool hasRotation = dot(rotation, rotation) > 1e-6;
    float3 reference = hasRotation ? qRotateVec3(float3(0, 1, 0), normalize(rotation)) : float3(0, 1, 0);
    float3 normal = reference - dot(reference, tangent) * tangent;
    if (dot(normal, normal) < 1e-4)
    {
        reference = hasRotation ? qRotateVec3(float3(0, 0, 1), normalize(rotation)) : float3(0, 0, 1);
        normal = reference - dot(reference, tangent) * tangent;
    }

    return SafeNormalize(normal, AnyPerpendicular(tangent));
}

/** Carries the ring normal from one point to the next with the double reflection method (Wang et al. 2008). */
float3 TransportNormal(float3 normal, float3 tangent, float3 position, float3 nextPosition, float3 nextTangent)
{
    float3 step = nextPosition - position;
    float stepLengthSquared = dot(step, step);
    if (stepLengthSquared > 1e-12)
    {
        normal -= (2 / stepLengthSquared) * dot(step, normal) * step;
        tangent -= (2 / stepLengthSquared) * dot(step, tangent) * step;
    }

    float3 tangentChange = nextTangent - tangent;
    float tangentChangeSquared = dot(tangentChange, tangentChange);
    if (tangentChangeSquared > 1e-12)
        normal -= (2 / tangentChangeSquared) * dot(tangentChange, normal) * tangentChange;

    normal -= dot(normal, nextTangent) * nextTangent;
    return SafeNormalize(normal, AnyPerpendicular(nextTangent));
}

/** Threads of the single group that measures distances across the whole buffer. */
static const int DistanceThreadCount = 1024;

groupshared float ChunkStartDistances[DistanceThreadCount];
groupshared float TotalDistance;

float SegmentLength(int i, uint pointCount)
{
    if (!IsLinePoint(i, pointCount) || !IsLinePoint(i + 1, pointCount))
        return 0;

    return length(Points[i + 1].Position - Points[i].Position);
}

// Distances that continue across separators. One group scans the whole buffer: each thread sums a chunk of
// segments, the chunk sums are scanned in shared memory, then each thread writes the running distance of its chunk.
// Writes only Line.yz, which csMain leaves alone, so the two passes don't depend on each other's order.
[numthreads(1024, 1, 1)]
void csBufferDistances(uint3 threadId : SV_GroupThreadID)
{
    uint pointCount, pointStride;
    Points.GetDimensions(pointCount, pointStride);

    int chunkSize = ((int)pointCount + DistanceThreadCount - 1) / DistanceThreadCount;
    int chunkStart = (int)threadId.x * chunkSize;
    int chunkEnd = min(chunkStart + chunkSize, (int)pointCount);

    float chunkLength = 0;
    for (int summedIndex = chunkStart; summedIndex < chunkEnd; summedIndex++)
        chunkLength += SegmentLength(summedIndex, pointCount);

    ChunkStartDistances[threadId.x] = chunkLength;
    GroupMemoryBarrierWithGroupSync();

    if (threadId.x == 0)
    {
        float distanceSoFar = 0;
        for (int chunkIndex = 0; chunkIndex < DistanceThreadCount; chunkIndex++)
        {
            float length = ChunkStartDistances[chunkIndex];
            ChunkStartDistances[chunkIndex] = distanceSoFar;
            distanceSoFar += length;
        }

        TotalDistance = distanceSoFar;
    }

    GroupMemoryBarrierWithGroupSync();

    float distance = ChunkStartDistances[threadId.x];
    float totalDistance = TotalDistance;
    for (int writtenIndex = chunkStart; writtenIndex < chunkEnd; writtenIndex++)
    {
        OutRingFrames[writtenIndex].Line.yz = float2(distance, totalDistance);
        distance += SegmentLength(writtenIndex, pointCount);
    }
}

// One thread walks each line, as every frame depends on the one before. Threads of other points return at once.
[numthreads(64, 1, 1)]
void csMain(uint3 id : SV_DispatchThreadID)
{
    uint pointCount, pointStride;
    Points.GetDimensions(pointCount, pointStride);

    int startIndex = (int)id.x;
    if (startIndex >= (int)pointCount)
        return;

    // Line.yz belong to csBufferDistances, so this pass leaves them alone.
    if (!IsLinePoint(startIndex, pointCount))
    {
        OutRingFrames[startIndex].NormalAndDistance = 0;
        OutRingFrames[startIndex].Line.x = 0;
        return;
    }

    if (IsLinePoint(startIndex - 1, pointCount))
        return;

    float3 position = Points[startIndex].Position;
    float3 tangent = CenterlineTangent(startIndex, pointCount);
    float3 normal = SeedNormal(Points[startIndex], tangent);
    float distanceAlongLine = 0;
    OutRingFrames[startIndex].NormalAndDistance = float4(normal, distanceAlongLine);

    int endIndex = startIndex;
    for (int pointIndex = startIndex + 1; pointIndex < (int)pointCount; pointIndex++)
    {
        if (IsSeparator(Points[pointIndex]))
            break;

        float3 nextPosition = Points[pointIndex].Position;
        float3 nextTangent = CenterlineTangent(pointIndex, pointCount);
        normal = TransportNormal(normal, tangent, position, nextPosition, nextTangent);
        distanceAlongLine += length(nextPosition - position);
        OutRingFrames[pointIndex].NormalAndDistance = float4(normal, distanceAlongLine);

        position = nextPosition;
        tangent = nextTangent;
        endIndex = pointIndex;
    }

    // The line's length is known only at its end, so a second walk hands it to every point for normalized texturing.
    for (int lineIndex = startIndex; lineIndex <= endIndex; lineIndex++)
        OutRingFrames[lineIndex].Line.x = distanceAlongLine;
}
