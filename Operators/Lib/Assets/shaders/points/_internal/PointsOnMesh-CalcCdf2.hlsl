#include "shared/pbr.hlsl"

struct FaceProperties {
    float normalizedFaceArea;
    float cdf;
};

cbuffer EmitParameter : register(b0)
{
    float UseVertexSelection;
};


StructuredBuffer<PbrVertex> Vertices : register(t0);
StructuredBuffer<int3> FaceIndices : register(t1);

RWStructuredBuffer<FaceProperties> FaceData : register(u0);

// Building the CDF is sequential, so one thread computes the whole buffer (dispatched with 1,1,1).
[numthreads(1,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint faceCount, stride;
    FaceData.GetDimensions(faceCount, stride);

    uint indexCount, indexStride;
    FaceIndices.GetDimensions(indexCount, indexStride);
    faceCount = min(faceCount, indexCount);

    if (i.x >= faceCount)
        return;

    float totalArea = 0;

    // One declaration for all loops: the loop variable lives in the function's scope.
    uint j;
    for (j = 0; j < faceCount; j++)
    {
        uint3 f = FaceIndices[j];
        float3 p0 = Vertices[f[0]].Position;
        float3 p1 = Vertices[f[1]].Position;
        float3 p2 = Vertices[f[2]].Position;

        float3 baseDir = p1 - p0;
        float a = length(baseDir);
        baseDir = normalize(baseDir);

        float3 heightStart = p0 + dot(p2 - p0, baseDir) * baseDir;
        float b = length(p2 - heightStart);
        float faceArea = a * b * 0.5;
        faceArea = isnan(faceArea) ? 0 : faceArea;

        float selection = UseVertexSelection > 0.5 
                        ? (Vertices[f[0]].Selected
                        + Vertices[f[1]].Selected
                        + Vertices[f[2]].Selected) 
                        : 1;

        float weightedArea = faceArea * selection;
        FaceData[j].normalizedFaceArea = weightedArea;
        totalArea += weightedArea;
    }

    // Degenerate meshes or empty vertex selections would divide by zero. In that case spread the
    // points evenly over the faces instead of producing NaNs.
    float cdf = 0;
    for (j = 0; j < faceCount; j++)
    {
        float normalizedArea = totalArea > 0
                                   ? FaceData[j].normalizedFaceArea / totalArea
                                   : 1.0 / (float)faceCount;
        cdf += normalizedArea;
        FaceData[j].cdf = cdf;
    }
}

