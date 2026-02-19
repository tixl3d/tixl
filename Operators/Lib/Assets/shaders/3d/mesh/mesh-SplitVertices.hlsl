#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

// cbuffer Transforms : register(b0)
// {
//     float4x4 CameraToClipSpace;
//     float4x4 ClipSpaceToCamera;
//     float4x4 WorldToCamera;
//     float4x4 CameraToWorld;
//     float4x4 WorldToClipSpace;
//     float4x4 ClipSpaceToWorld;
//     float4x4 ObjectToWorld;
//     float4x4 WorldToObject;
//     float4x4 ObjectToCamera;
//     float4x4 ObjectToClipSpace;
// };

cbuffer Params : register(b0)
{
    float ShadeFlat;
}

StructuredBuffer<int3> SourceIndices : t0;
StructuredBuffer<PbrVertex> SourceVertices : t1;

RWStructuredBuffer<int3> ResultIndices : u0;
RWStructuredBuffer<PbrVertex> ResultVertices : u1;


[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numFaces, stride;
    SourceIndices.GetDimensions(numFaces, stride);
    int faceIndex = i.x;
    if(faceIndex >= numFaces) {
        return;
    }

    int3 indicesForFace = SourceIndices[faceIndex];
    
    int vertexIndex = faceIndex* 3;

    float3 p1= SourceVertices[indicesForFace.x].Position;
    float3 p2= SourceVertices[indicesForFace.y].Position;
    float3 p3= SourceVertices[indicesForFace.z].Position;

    // float a1= dot( normalize(p2-p1), normalize(p3-p1));
    // float a2= dot( normalize(p1-p2), normalize(p3-p2));
    // float a3= dot( normalize(p1-p3), normalize(p1-p3));
    
    float a1= dot( (p2-p1), (p3-p1));
    float a2= dot( (p1-p2), (p3-p2));
    float a3= dot( (p1-p3), (p1-p3));

    float3 w = float3(1,1,1);

    if( abs(a1 - 1) < 0.2) {
        w = float3(0,1,1);
    }
    else if( abs(a2 - 1) < 0.2) {
        w = float3(1,0,1);
    }
    else if( abs(a3 - 1) < 0.2) {
        w = float3(1,1,0);
    }

    float flat = ShadeFlat;

    ResultVertices[vertexIndex+0] = SourceVertices[indicesForFace.x];
    ResultVertices[vertexIndex+1] = SourceVertices[indicesForFace.y];
    ResultVertices[vertexIndex+2] = SourceVertices[indicesForFace.z];

    float3 n1= SourceVertices[indicesForFace.x].Normal;
    float3 n2= SourceVertices[indicesForFace.y].Normal;
    float3 n3= SourceVertices[indicesForFace.z].Normal;
    float3 n = normalize(n1 * w.x + n2 * w.y + n3 * w.z);

    ResultVertices[vertexIndex+0].Normal = lerp(n1,n, flat);
    ResultVertices[vertexIndex+1].Normal = lerp(n2,n, flat);
    ResultVertices[vertexIndex+2].Normal = lerp(n3,n, flat);

    float3 b1= SourceVertices[indicesForFace.x].Bitangent;
    float3 b2= SourceVertices[indicesForFace.y].Bitangent;
    float3 b3= SourceVertices[indicesForFace.z].Bitangent;
    float3 b = normalize(b1 * w.x + b2 * w.y + b3 * w.z);

    ResultVertices[vertexIndex+0].Bitangent = lerp(b1, b, flat);
    ResultVertices[vertexIndex+1].Bitangent = lerp(b2, b, flat);
    ResultVertices[vertexIndex+2].Bitangent = lerp(b3, b, flat);

    float3 t1= SourceVertices[indicesForFace.x].Tangent;
    float3 t2= SourceVertices[indicesForFace.y].Tangent;
    float3 t3= SourceVertices[indicesForFace.z].Tangent;
    float3 t = normalize(t1 * w.x + t2 * w.y +t3 * w.z);
    ResultVertices[vertexIndex+0].Tangent = lerp(t1, t, flat);
    ResultVertices[vertexIndex+1].Tangent = lerp(t2, t, flat);
    ResultVertices[vertexIndex+2].Tangent = lerp(t3, t, flat);




    ResultIndices[faceIndex] = int3(vertexIndex, vertexIndex+1, vertexIndex+2);
}

