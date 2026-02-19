
static const float3 Quad[] = 
{
  float3(-1, -1, 0),
  float3( 1, -1, 0), 
  float3( 1,  1, 0), 
  float3( 1,  1, 0), 
  float3(-1,  1, 0), 
  float3(-1, -1, 0), 
};


cbuffer Transforms : register(b0)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

cbuffer Params : register(b1)
{
    float4 Color;
    float Width;
    float Height;
};

struct vsOutput
{
    float4 position : SV_POSITION;
    float4 posInWorld : POSITION;
    float2 texCoord : TEXCOORD;
};

vsOutput vsMain(uint vertexId: SV_VertexID)
{
    vsOutput output;
    float2 quadVertex = Quad[vertexId].xy;
    float2 quadVertexInObject = quadVertex * float2(Width, Height);
    output.posInWorld = mul(float4(quadVertexInObject, 0, 1), ObjectToWorld);
    output.position = mul(output.posInWorld, WorldToClipSpace);
    output.texCoord = quadVertex*float2(0.5, -0.5) + 0.5;

    return output;
}
