#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformMatrix;
    float UseVertexSelection;
}

StructuredBuffer<PbrVertex> SourceVerts : register(t0);        
RWStructuredBuffer<PbrVertex> ResultVerts : register(u0);   


[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourceVerts.GetDimensions(numStructs, stride);
    if(i.x >= numStructs) {
        return;
    }

    float s = UseVertexSelection > 0.5 ? SourceVerts[i.x].Selected : 1;

    float3 pos = SourceVerts[i.x].Position;
    ResultVerts[i.x].Position = lerp(pos, mul(float4(pos,1), TransformMatrix).xyz, s);

    // Normals need the inverse transpose of the upper 3x3 to survive non-uniform scale.
    // Cofactor rows (row-vector convention, matching mul(v, M)):
    float3 r0 = TransformMatrix[0].xyz;
    float3 r1 = TransformMatrix[1].xyz;
    float3 r2 = TransformMatrix[2].xyz;
    float3 c0 = cross(r1, r2);
    float3 c1 = cross(r2, r0);
    float3 c2 = cross(r0, r1);
    float det = dot(r0, c0);

    float3 normal = SourceVerts[i.x].Normal;
    float3 newNormal = normal.x * c0 + normal.y * c1 + normal.z * c2;
    newNormal *= (det < 0) ? -1 : 1;    // keep orientation correct for mirroring
    float len = length(newNormal);
    newNormal = len > 1e-12 ? newNormal / len : normal;
    ResultVerts[i.x].Normal = lerp(normal, newNormal, s);

    float3 tangent = SourceVerts[i.x].Tangent;
    ResultVerts[i.x].Tangent = lerp(tangent, normalize(mul(float4(tangent,0), TransformMatrix).xyz), s);

    float3 bitangent = SourceVerts[i.x].Bitangent;
    ResultVerts[i.x].Bitangent = lerp(bitangent, normalize(mul(float4(bitangent,0), TransformMatrix).xyz), s);

    ResultVerts[i.x].TexCoord = SourceVerts[i.x].TexCoord;
    ResultVerts[i.x].TexCoord2 = SourceVerts[i.x].TexCoord2;

    ResultVerts[i.x].Selected = SourceVerts[i.x].Selected;
    ResultVerts[i.x].ColorRGB = SourceVerts[i.x].ColorRGB;
}

