#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 Center;
    float Amount;
    float3 UpVector;
    float UseWAsWeight;
    float Flip;
}

StructuredBuffer<Point> SourcePoints : register(t0);
RWStructuredBuffer<Point> ResultPoints : register(u0);

// Aligns orientation quaternion q so that its +Z forward
// points towards newForward
float4 qAlignForward(float4 q, float3 newForward)
{
    newForward = normalize(-newForward);

    // old up from current orientation (+Y rotated by q)
    float3 oldUp = qRotateVec3(float3(0, 1, 0), q);

    // project old up onto plane perpendicular to newForward
    float3 projUp = oldUp - newForward * dot(oldUp, newForward);

    // handle degeneracy: if oldUp ~ parallel to newForward
    if (length(projUp) < 1e-5)
    {
        projUp = abs(newForward.x) < 0.9 ? float3(1, 0, 0) : float3(0, 1, 0);
        projUp = projUp - newForward * dot(projUp, newForward);
    }
    projUp = normalize(projUp);

    // Right-handed orthonormal basis:
    float3 ez = normalize(newForward);        // forward (+Z)
    float3 ex = normalize(cross(projUp, ez)); // right (+X)
    float3 ey = normalize(cross(ez, ex));     // up (+Y), fixed orthogonal

    // Orientation matrix (columns = basis vectors)
    float3x3 m = float3x3(ex, ey, ez);

    // Convert to quaternion
    return normalize(qFromMatrix3Precise(m));
}

// Aligns orientation quaternion q so that its +Z forward
// points towards newForward
float4 qAlignForward2(float4 q, float3 newForward)
{
    newForward = normalize(newForward);

    // old up from current orientation (+Y rotated by q)
    float3 oldUp = qRotateVec3(float3(0, 1, 0), q);

    // project old up onto plane perpendicular to newForward
    float3 projUp = oldUp - newForward * dot(oldUp, newForward);

    // handle degenerate case: oldUp nearly parallel to newForward
    if (length(projUp) < 1e-5)
    {
        projUp = normalize(abs(newForward.x) < 0.9 ? float3(1, 0, 0) : float3(0, 1, 0));
        projUp = normalize(projUp - newForward * dot(projUp, newForward));
    }
    else
    {
        projUp = normalize(projUp);
    }

    // rebuild quaternion with forward = newForward, up ≈ projected up
    return qLookAt(newForward, -projUp);
}

float4 qAlignForward3(float4 q, float3 newForward)
{
    newForward = normalize(newForward);

    // old up from current orientation (+Y rotated by q)
    float3 oldUp = qRotateVec3(float3(0, 1, 0), q);

    // project old up onto plane perpendicular to newForward
    float3 projUp = oldUp - newForward * dot(oldUp, newForward);

    // handle degeneracy
    if (length(projUp) < 1e-5)
    {
        projUp = abs(newForward.x) < 0.9 ? float3(1, 0, 0) : float3(0, 1, 0);
        projUp = projUp - newForward * dot(projUp, newForward);
    }
    projUp = normalize(projUp);

    // Build explicit right-handed basis
    float3 ez = newForward;                   // +Z = forward
    float3 ex = normalize(cross(projUp, ez)); // +X = right
    float3 ey = normalize(cross(ez, ex));     // +Y = up

    float3x3 m = float3x3(ex, ey, ez);

    return normalize(qFromMatrix3Precise(m));
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x;
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);

    if (index >= numStructs)
        return;

    if (IsSeparator(SourcePoints[index]))
        return;

    // Find neighbours
    uint prevIndex = index;
    uint nextIndex = index;

    if (index > 0 && !IsSeparator(SourcePoints[index - 1]))
    {
        prevIndex--;
    }

    if (index <= numStructs - 1 && !IsSeparator(SourcePoints[index + 1]))
    {
        nextIndex++;
    }

    // Nothing to align
    if (prevIndex == nextIndex)
        return;

    float3 v = SourcePoints[nextIndex].Position - SourcePoints[prevIndex].Position;

    // Points fall together
    float l = length(v);

    if (l < 0.0001)
        return;

    float3 dir = v / l;
    Point p = SourcePoints[index];

    float4 r = p.Rotation;
    //  Attempt to smooth orientaion with neighbours didn't yield significant improvements
    // float4 r = qSlerp(p.Rotation, qSlerp(SourcePoints[prevIndex - 1].Rotation, SourcePoints[nextIndex + 1].Rotation, 0.5), 1.0);

    p.Rotation = qSlerp(p.Rotation, qAlignForward2(r, dir), Amount);
    ResultPoints[i.x] = p;
}
