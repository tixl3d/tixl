#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float3 Stretch;
    float Size;
    float ApplyScale;
}

cbuffer Params : register(b1)
{
    int PointCount;
    int ScaleFX;
}

StructuredBuffer<PbrVertex> SourceVertices : t0;
StructuredBuffer<Point> Points : t1;

RWStructuredBuffer<PbrVertex> ResultVertices : u0;

[numthreads(16, 16, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint vertexIndex = i.x;
    uint pointIndex = i.y;
    uint sourcePointCount, sourceVertexCount, stride;

    Points.GetDimensions(sourcePointCount, stride);
    SourceVertices.GetDimensions(sourceVertexCount, stride);

    if (pointIndex >= sourcePointCount || vertexIndex >= sourceVertexCount)
    {
        return;
    }

    int targetVertexIndex = pointIndex * sourceVertexCount + vertexIndex;

    PbrVertex v = SourceVertices[vertexIndex];

    Point p = Points[pointIndex];

    // Apply point transform
    float4 posInObject = float4(v.Position, 1);

    float4x4 orientationMatrix = transpose(qToMatrix(p.Rotation));

    float sizeFxFactor = ScaleFX == 0
                             ? 1
                         : (ScaleFX == 1) ? p.FX1
                                          : p.FX2;

    // float3 resizeFromW = UseWForSize ?  (p.FX1 *  p.Scale) :1;
    float3 resizeFromScale = ApplyScale ? p.Scale : 1;

    posInObject.xyz *= max(0, resizeFromScale) * Stretch * Size * sizeFxFactor;

    posInObject = mul(float4(posInObject.xyz, 1), orientationMatrix);

    posInObject += float4(p.Position, 0);

    v.Position = posInObject;
    v.Normal = qRotateVec3(v.Normal, p.Rotation);
    v.Tangent = qRotateVec3(v.Tangent, p.Rotation);
    v.Bitangent = qRotateVec3(v.Bitangent, p.Rotation);
    ResultVertices[targetVertexIndex] = v;
}
