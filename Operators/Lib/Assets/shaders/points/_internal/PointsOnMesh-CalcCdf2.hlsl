#include "shared/pbr.hlsl"

struct FaceProperties {
    float normalizedFaceArea;
    float cdf;
};

cbuffer EmitParameter : register(b0)
{
    float UseVertexSelection;
};


StructuredBuffer<PbrVertex> Vertices : t0;
StructuredBuffer<int3> FaceIndices : t1;

RWStructuredBuffer<FaceProperties> FaceData : u0;

[numthreads(1,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint faceCount, stride;
    FaceData.GetDimensions(faceCount, stride);
    if (i.x >= (uint)faceCount)
        return;

    float sum;
    for (int j = 0; j < faceCount; j++)
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

        FaceData[j].normalizedFaceArea =  faceArea * selection;
        sum += faceArea;
    }

    sum = 0;
    for (int j = 0; j < faceCount; j++)
    {
        sum += FaceData[j].normalizedFaceArea;
    }

    sum = 1.0/sum;

    float cdf = 0;
    for (int j = 0; j < faceCount; j++)
    {
        cdf += FaceData[j].normalizedFaceArea * sum;
        FaceData[j].cdf = cdf;
    }
}

