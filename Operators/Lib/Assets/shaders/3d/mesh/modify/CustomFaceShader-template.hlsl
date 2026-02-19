#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float A;
    float B;
    float C;
    float D;
    float3 Offset;
}

StructuredBuffer<PbrVertex> SourceVertices : t0;
StructuredBuffer<int3> SourceIndices : t1;

Texture2D<float4> Image : register(t2);
Texture2D<float4> Gradient : register(t3);

sampler Sampler : register(s0);
sampler ClampedSampler : register(s1);

RWStructuredBuffer<PbrVertex> ResultVertices : u0;
RWStructuredBuffer<int3> ResultIndices : u1;

//- DEFINES ------------------------------------
/*{defines}*/
//----------------------------------------------

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numFaces, stride;
    SourceIndices.GetDimensions(numFaces, stride);
    uint faceIndex = i.x;
    if(faceIndex >= numFaces) {
        return;
    }


    int3 faceIndices = SourceIndices[faceIndex];
    PbrVertex v1= SourceVertices[faceIndices.x];
    PbrVertex v2= SourceVertices[faceIndices.y];
    PbrVertex v3= SourceVertices[faceIndices.z];

//- METHOD -------------------------------------
/*{method}*/
//----------------------------------------------

    ResultVertices[faceIndices.x] = v1;
    ResultVertices[faceIndices.y] = v2;
    ResultVertices[faceIndices.z] = v3;
    
    int vertexIndex = faceIndex* 3;
    ResultIndices[faceIndex] = int3(vertexIndex, vertexIndex+1, vertexIndex+2);
}

