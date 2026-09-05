#include "shared/pbr.hlsl"

struct SkinWeightDef
{
    int4 JointIndices;
    float4 Weights;
};

// Rows of a System.Numerics row-vector matrix (v' = v * M), stored explicitly
// to avoid HLSL's column-major reinterpretation of float4x4 in structured buffers.
struct SkinMatrix
{
    float4 Row0;
    float4 Row1;
    float4 Row2;
    float4 Row3;
};

StructuredBuffer<PbrVertex> SourceVertices : register(t0);
StructuredBuffer<SkinWeightDef> SkinWeights : register(t1);
StructuredBuffer<SkinMatrix> SkinMatrices : register(t2);

RWStructuredBuffer<PbrVertex> ResultVertices : register(u0);

float4 ApplyMatrix(float4 v, SkinMatrix m)
{
    return v.x * m.Row0 + v.y * m.Row1 + v.z * m.Row2 + v.w * m.Row3;
}

[numthreads(64, 1, 1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint vertexCount, stride;
    SourceVertices.GetDimensions(vertexCount, stride);
    if (i.x >= vertexCount)
        return;

    PbrVertex v = SourceVertices[i.x];
    SkinWeightDef skin = SkinWeights[i.x];

    float totalWeight = dot(skin.Weights, 1);
    if (totalWeight < 0.0001)
    {
        ResultVertices[i.x] = v;
        return;
    }

    float3 position = 0;
    float3 normal = 0;
    float3 tangent = 0;
    float3 bitangent = 0;

    [unroll]
    for (int influence = 0; influence < 4; influence++)
    {
        float weight = skin.Weights[influence];
        if (weight <= 0)
            continue;

        SkinMatrix m = SkinMatrices[skin.JointIndices[influence]];
        position += ApplyMatrix(float4(v.Position, 1), m).xyz * weight;
        normal += ApplyMatrix(float4(v.Normal, 0), m).xyz * weight;
        tangent += ApplyMatrix(float4(v.Tangent, 0), m).xyz * weight;
        bitangent += ApplyMatrix(float4(v.Bitangent, 0), m).xyz * weight;
    }

    v.Position = position;
    v.Normal = length(normal) > 0.0001 ? normalize(normal) : v.Normal;
    v.Tangent = length(tangent) > 0.0001 ? normalize(tangent) : v.Tangent;
    v.Bitangent = length(bitangent) > 0.0001 ? normalize(bitangent) : v.Bitangent;

    ResultVertices[i.x] = v;
}
