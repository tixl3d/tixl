#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float A;
    float B;
    float C;
    float D;
    float3 Offset;
    float __padding;
    float2 GainAndBias;
}

cbuffer Params : register(b1)
{
/*{FLOAT_PARAMS}*/
}

StructuredBuffer<PbrVertex> SourceVertices : register(t0);
StructuredBuffer<int3> SourceIndices : register(t1);

Texture2D<float4> Image : register(t2);
Texture2D<float4> Gradient : register(t3);

sampler Sampler : register(s0);
sampler ClampedSampler : register(s1);

RWStructuredBuffer<PbrVertex> ResultVertices : register(u0);
RWStructuredBuffer<int3> ResultIndices : register(u1);


//=== Additional Resources ==========================================
/*{RESOURCES(t4)}*/

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------
float4 GetField(float4 p)
{
    float4 f = 1;
    /*{FIELD_CALL}*/
    return f;
}

inline float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}

float3 GetFieldNormal(float3 p, float d=0.001)
{
    return normalize(
        GetDistance(p + float3(d, -d, -d)) * float3(1, -1, -1) +
        GetDistance(p + float3(-d, d, -d)) * float3(-1, 1, -1) +
        GetDistance(p + float3(-d, -d, d)) * float3(-1, -1, 1) +
        GetDistance(p + float3(d, d, d)) * float3(1, 1, 1));
}

//===================================================================


//- DEFINES ------------------------------------
/*{defines}*/
//----------------------------------------------

float Biased(float f){return ApplyGainAndBias(f, GainAndBias);}
float4 SampleGradient(float f){return Gradient.SampleLevel(ClampedSampler, float2(f, 0.5), 0);}


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

    float3 pos1 = SourceVertices[faceIndices.x].Position;
    float3 pos2 = SourceVertices[faceIndices.y].Position;
    float3 pos3 = SourceVertices[faceIndices.z].Position;
{
//- METHOD -------------------------------------
/*{method}*/
//----------------------------------------------
}

    ResultVertices[faceIndices.x] = v1;
    ResultVertices[faceIndices.y] = v2;
    ResultVertices[faceIndices.z] = v3;
    
    int vertexIndex = faceIndex* 3;
    ResultIndices[faceIndex] = int3(vertexIndex, vertexIndex+1, vertexIndex+2);
}

