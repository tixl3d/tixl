// Extracts the transform matrix and colors from points so they can be
// used more efficently in shaders like RepeatFieldAtPoints.

#include "shared/point.hlsl"

cbuffer Params : register(b0)
{
    // no params necessary
}

struct PointTransform
{
    float4x4 WorldToPointObject;
    float4 PointColor;
};

StructuredBuffer<Point> SourcePoints : t0;
RWStructuredBuffer<PointTransform> Results : u0;

float3x3 QuaternionToMatrix_RH(float4 q)
{
    float x2 = q.x + q.x, y2 = q.y + q.y, z2 = q.z + q.z;
    float xx = q.x * x2, yy = q.y * y2, zz = q.z * z2;
    float xy = q.x * y2, xz = q.x * z2, yz = q.y * z2;
    float wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;

    return float3x3(
        1 - (yy + zz), xy - wz, xz + wy,
        xy + wz, 1 - (xx + zz), yz - wx,
        xz - wy, yz + wx, 1 - (xx + yy));
}

float4x4 GetWorldToObjectMatrix(float3 position, float4 rotation, float3 scale)
{
    float3x3 R = QuaternionToMatrix_RH(normalize(rotation));

    // Inverse rotation and scale
    float3x3 invRS;
    invRS[0] = R[0] / scale.x;
    invRS[1] = R[1] / scale.y;
    invRS[2] = R[2] / scale.z;
    invRS = transpose(invRS); // inverse rotation

    // Inverse translation
    float3 invT = -mul(invRS, position);

    // Full world-to-object matrix
    return float4x4(
        invRS[0].x, invRS[1].x, invRS[2].x, 0,
        invRS[0].y, invRS[1].y, invRS[2].y, 0,
        invRS[0].z, invRS[1].z, invRS[2].z, 0,
        invT.x, invT.y, invT.z, 1);
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint resultsCount, sourcePointCount, targetPointCount, stride;
    SourcePoints.GetDimensions(sourcePointCount, stride);
    Results.GetDimensions(resultsCount, stride);

    uint index = i.x;
    if (index >= resultsCount)
    {
        return;
    }

    Point sourceP = SourcePoints[index];

    Results[index].WorldToPointObject = GetWorldToObjectMatrix(sourceP.Position, sourceP.Rotation, sourceP.Scale);
    Results[index].PointColor = sourceP.Color;
}
