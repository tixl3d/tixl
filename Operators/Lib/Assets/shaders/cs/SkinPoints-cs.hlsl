#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

struct SkinWeightDef
{
    int4 JointIndices;
    float4 Weights;
};

// Rows of a System.Numerics row-vector matrix (v' = v * M) - see SkinMeshVertices-cs.hlsl
struct SkinMatrix
{
    float4 Row0;
    float4 Row1;
    float4 Row2;
    float4 Row3;
};

StructuredBuffer<Point> SourcePoints : register(t0);
StructuredBuffer<SkinWeightDef> SkinWeights : register(t1);
StructuredBuffer<SkinMatrix> SkinMatrices : register(t2);

RWStructuredBuffer<Point> ResultPoints : register(u0);

float4 ApplyMatrix(float4 v, SkinMatrix m)
{
    return v.x * m.Row0 + v.y * m.Row1 + v.z * m.Row2 + v.w * m.Row3;
}

[numthreads(64, 1, 1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    SourcePoints.GetDimensions(pointCount, stride);
    if (i.x >= pointCount)
        return;

    Point p = SourcePoints[i.x];
    SkinWeightDef skin = SkinWeights[i.x];

    float totalWeight = dot(skin.Weights, 1);
    if (isnan(p.Scale.x) || totalWeight < 0.0001)
    {
        ResultPoints[i.x] = p; // separators and unbound points pass through
        return;
    }

    float3 position = 0;
    float4 row0 = 0;
    float4 row1 = 0;
    float4 row2 = 0;

    [unroll]
    for (int influence = 0; influence < 4; influence++)
    {
        float weight = skin.Weights[influence];
        if (weight <= 0)
            continue;

        SkinMatrix m = SkinMatrices[skin.JointIndices[influence]];
        position += ApplyMatrix(float4(p.Position, 1), m).xyz * weight;
        row0 += m.Row0 * weight;
        row1 += m.Row1 * weight;
        row2 += m.Row2 * weight;
    }

    p.Position = position;

    // Orthonormalized rotation part of the blended matrix, applied to the point's orientation
    float3x3 rotation = float3x3(normalize(row0.xyz), normalize(row1.xyz), normalize(row2.xyz));
    float4 skinRotation = normalize(qFromMatrix3Precise(rotation));
    p.Rotation = normalize(qMul(skinRotation, p.Rotation));

    ResultPoints[i.x] = p;
}
