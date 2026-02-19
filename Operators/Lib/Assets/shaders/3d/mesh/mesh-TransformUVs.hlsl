#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformMatrix;
    float UseVertexSelection;
    float ToTexCoord2;
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
    float3 pos = float3(SourceVerts[i.x].TexCoord, 0);
    float3 pos2 = float3(SourceVerts[i.x].TexCoord2, 0);
    ResultVerts[i.x] = SourceVerts[i.x];
    if((bool)ToTexCoord2 == true){
        ResultVerts[i.x].TexCoord2 = lerp(pos2, mul(float4(pos2,1), TransformMatrix).xyz, s).xy;  
    }
    else{
        ResultVerts[i.x].TexCoord = lerp(pos, mul(float4(pos,1), TransformMatrix).xyz, s).xy;
    }
    
}

