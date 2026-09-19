
#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/particle.hlsl"

cbuffer CountConstants : register(b0)
{
    int4 bufferCount;
};

struct Face
{
    float3 positions[3];
    float2 texCoords[3];
    float3 normals[3];
    int id;
    float normalizedFaceArea;
    float cdf;
};


RWStructuredBuffer<Face> SlicedData : u0;

[numthreads(1,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SlicedData.GetDimensions(numStructs, stride);
    if (i.x >= (uint)numStructs)
        return; 
    
    uint index = i.x;

    int size = bufferCount;

    float sum;
    for (int j = 0; j < size; j++)
    {
        Face f = SlicedData[j];
        float3 baseDir = f.positions[1] - f.positions[0];
        float a = length(baseDir);
        baseDir = normalize(baseDir);

        float3 heightStart = f.positions[0] + dot(f.positions[2] - f.positions[0], baseDir) * baseDir;
        float b = length(f.positions[2] - heightStart);
        float faceArea = a * b * 0.5;
        SlicedData[j].normalizedFaceArea = faceArea;
        sum += faceArea;
    }

    sum = 0;
    for (int j = 0; j < size; j++)
    {
        sum += SlicedData[j].normalizedFaceArea;
    }

    sum = 1.0/sum;

    float cdf = 0;
    for (int j = 0; j < size; j++)
    {
        cdf += SlicedData[j].normalizedFaceArea * sum;
        SlicedData[j].cdf = cdf;
    }
}

