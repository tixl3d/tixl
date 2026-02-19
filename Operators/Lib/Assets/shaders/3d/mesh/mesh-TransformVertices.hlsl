#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformMatrix;
    float UseVertexSelection;
}

StructuredBuffer<PbrVertex> SourceVerts : t0;        
RWStructuredBuffer<PbrVertex> ResultVerts : u0;   


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

    float3 normal = SourceVerts[i.x].Normal;
    ResultVerts[i.x].Normal = lerp(normal,normalize(mul(float4(normal,0), TransformMatrix).xyz), s);

    float3 tangent = SourceVerts[i.x].Tangent;
    ResultVerts[i.x].Tangent = lerp(tangent,normalize(mul(float4(tangent,0), TransformMatrix).xyz), s);

    float3 bitangent = SourceVerts[i.x].Bitangent;
    ResultVerts[i.x].Bitangent = lerp(bitangent, normalize(mul(float4(bitangent,0), TransformMatrix).xyz), s);

    ResultVerts[i.x].TexCoord = SourceVerts[i.x].TexCoord;
    ResultVerts[i.x].TexCoord2 = SourceVerts[i.x].TexCoord2;

    ResultVerts[i.x].Selected = SourceVerts[i.x].Selected;
    ResultVerts[i.x].ColorRGB = SourceVerts[i.x].ColorRGB;
}

