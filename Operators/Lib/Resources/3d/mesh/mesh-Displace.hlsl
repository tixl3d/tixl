#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float Mode;
    float Amount;
    float2 ScaleUV;

    float3 Distribution;
    float UseVertexSelection;

    float3 MainOffset;
    float UvSelection;
}

StructuredBuffer<PbrVertex> SourceVertices : t0;        
Texture2D<float4> DisplaceMap : register(t1);

RWStructuredBuffer<PbrVertex> ResultVertices : u0;   

sampler texSampler : register(s0);

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint gi = i.x;
    uint pointCount, _;
    SourceVertices.GetDimensions(pointCount, _);
    if(gi >= pointCount) {
        return;
    }

    PbrVertex v = SourceVertices[gi];
    ResultVertices[gi] = SourceVertices[gi];
    
    float weight = 1;

    float3 posInWorld = v.Position;
    float2 scaleuv = ScaleUV * float2(1, -1);
    float2 uv =SourceVertices[gi].TexCoord * scaleuv;

    if(UvSelection > 0.5) 
    {
        uv =SourceVertices[gi].TexCoord2 * scaleuv;
    }
    
    float4 texColor = DisplaceMap.SampleLevel(texSampler, uv, 0); 
    float3x3 TBN = float3x3(v.Tangent, v.Bitangent, v.Normal);
    
    
    float amount = Amount;
    if (UseVertexSelection > 0.5){
        amount = Amount * SourceVertices[gi].Selected;
    }
    
    float3 offset = 0;
    if(Mode < 0.5) 
    {
        offset = mul(
                (   
                    (texColor.r + texColor.g + texColor.b)/3 * texColor.a * float3(0,0,1) * Distribution 
                    + MainOffset
                ) * amount
        ,TBN);
    }
    else if(Mode< 1.5) 
    {
        offset= mul((texColor.rgb * texColor.a * Distribution + MainOffset) * amount, TBN);
    }
    else {
        offset= (texColor.rgb * texColor.a * Distribution + MainOffset)  * amount;
    } 

    ResultVertices[gi].Position = v.Position + offset;
}

