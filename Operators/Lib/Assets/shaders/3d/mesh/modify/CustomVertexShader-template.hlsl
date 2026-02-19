#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    float A;
    float B;
    float C;
    float D;
    float3 Offset;
}

cbuffer Params : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

StructuredBuffer<PbrVertex> SourceVertices : t0;
StructuredBuffer<int3> SourceIndices : t1;

Texture2D<float4> Image : register(t2);
Texture2D<float4> Gradient : register(t3);

sampler Sampler : register(s0);
sampler ClampedSampler : register(s1);

RWStructuredBuffer<PbrVertex> ResultVertices : u0;

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

//===================================================================


//- DEFINES ------------------------------------
/*{defines}*/
//----------------------------------------------

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numVertices, stride;
    SourceVertices.GetDimensions(numVertices, stride);
    uint vertexIndex = i.x;
    if(vertexIndex >= numVertices) {
        return;
    }

    PbrVertex v= SourceVertices[vertexIndex];

//- METHOD -------------------------------------
/*{method}*/
//----------------------------------------------

    ResultVertices[vertexIndex] = v;
}

