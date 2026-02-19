#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{

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
    
    ResultVerts[i.x].Position = SourceVerts[i.x].Position;
    ResultVerts[i.x].Normal = -SourceVerts[i.x].Normal;
    ResultVerts[i.x].Tangent = -SourceVerts[i.x].Tangent;
    ResultVerts[i.x].Bitangent = SourceVerts[i.x].Bitangent;
    ResultVerts[i.x].TexCoord = SourceVerts[i.x].TexCoord;
    ResultVerts[i.x].Selected = SourceVerts[i.x].Selected;
    ResultVerts[i.x].ColorRGB = SourceVerts[i.x].ColorRGB;
}

