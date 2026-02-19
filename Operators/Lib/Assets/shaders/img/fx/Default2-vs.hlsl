
static const float3 Quad[] = 
{
  float3(-1, -1, 0),
  float3( 1, -1, 0), 
  float3( 1,  1, 0), 
  float3( 1,  1, 0), 
  float3(-1,  1, 0), 
  float3(-1, -1, 0), 
};

cbuffer Params : register(b0)
{
    //float size;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

vsOutput vsMain(uint vertexId: SV_VertexID)
{
    vsOutput output;
    float4 quadPos = float4(Quad[vertexId], 1) ;
    output.texCoord = quadPos.xy * float2(0.5, -0.5) + 0.5;
    output.position = quadPos;
    return output; 
}
