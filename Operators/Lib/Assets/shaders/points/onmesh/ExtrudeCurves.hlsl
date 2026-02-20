#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    // float UseWAsWidth;
    float ScaleFactor;
    float UseStretch;
    float Width;
    float UVsDirection;
}

StructuredBuffer<Point> RailPoints : t0;
StructuredBuffer<Point> ShapePoints : t1;

RWStructuredBuffer<PbrVertex> Vertices : u0;
RWStructuredBuffer<int3> TriangleIndices : u1;

[numthreads(80, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint triangleCount, stride;
    TriangleIndices.GetDimensions(triangleCount, stride);

    uint vertexCount;
    Vertices.GetDimensions(vertexCount, stride);

    if (i.x >= vertexCount)
    {
        return;
    }

    uint rows;
    ShapePoints.GetDimensions(rows, stride);

    uint columns;
    RailPoints.GetDimensions(columns, stride);

    uint vertexIndex = i.x;
    uint rowIndex = vertexIndex % rows;
    uint columnIndex = vertexIndex / rows;

    PbrVertex v;
    Point railPoint = RailPoints[columnIndex];
    Point shapePoint = ShapePoints[rowIndex];

    // float3 scaleFactor = (UseStretch ? railPoint.Scale : 1) * (UseWAsWidth ? railPoint.FX1 : 1) * Width;
    float3 scaleFactor = (UseStretch ? railPoint.Scale : 1) * Width * (ScaleFactor == 0 ? 1 : (ScaleFactor == 1) ? railPoint.FX1
                                                                                                                 : railPoint.FX2);

    float4 rotation = normalize(qMul(railPoint.Rotation, shapePoint.Rotation));
    float3 position = qRotateVec3(shapePoint.Position * scaleFactor, railPoint.Rotation) + railPoint.Position;

    // float3 normal =

    v.Position = position;
    v.Normal = qRotateVec3(float3(0, 0, 1), rotation);
    v.Tangent = qRotateVec3(float3(0, 1, 0), rotation);
    v.Bitangent = qRotateVec3(float3(1, 0, 0), rotation);

    bool swapUVs = (UVsDirection > 0.5);
    float U = (float)columnIndex / max(1, columns - 1);
    float V = (float)rowIndex / max(1, rows - 1);

    if (swapUVs)
    {
        v.TexCoord = float2(V, U);
        v.TexCoord2 = float2(1.0 - U, V);
    }
    else
    {
        v.TexCoord = float2(1.0 - U, V);
        v.TexCoord2 = float2(V, U);
    }

    v.Selected = 1;
    v.ColorRGB = railPoint.Color.rgb * shapePoint.Color.rgb;

    Vertices[vertexIndex] = v;
    if (isnan(railPoint.Scale.x) || isnan(shapePoint.Scale.x))
        Vertices[vertexIndex].Position = float3(0, 0, 0);

    // Write face indices
    if (columnIndex < columns - 1 && rowIndex < rows - 1)
    {
        int faceIndex = 2 * (rowIndex + columnIndex * (rows - 1));

        if (
            isnan(railPoint.Scale.x) ||                   //
            isnan(RailPoints[columnIndex + 1].Scale.x) || //
            isnan(shapePoint.Scale.x) ||                  //
            isnan(ShapePoints[rowIndex + 1].Scale.x))
        {
            if (columnIndex < columns - 1 && rowIndex < rows - 1)
            {
                TriangleIndices[faceIndex + 0] = int3(0, 0, 0);
                TriangleIndices[faceIndex + 1] = int3(0, 0, 0);
                TriangleIndices[faceIndex + 1] = int3(0, 0, 0);
            }
            if (isnan(railPoint.Scale.x) || isnan(shapePoint.Scale.x))
                Vertices[vertexIndex].Position = float3(0, 0, 0);
            return;
        }

        TriangleIndices[faceIndex + 0] = int3(vertexIndex + 1, vertexIndex + rows, vertexIndex);
        TriangleIndices[faceIndex + 1] = int3(vertexIndex + 1, vertexIndex + rows + 1, vertexIndex + rows);
    }
}
