#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{

}

StructuredBuffer<int3> SourceIndices : t0;        
RWStructuredBuffer<int3> ResultIndices : u0;   

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourceIndices.GetDimensions(numStructs, stride);
    if(i.x >= numStructs) {
        return;
    }
    
    ResultIndices[i.x] = SourceIndices[i.x].zyx;
}

