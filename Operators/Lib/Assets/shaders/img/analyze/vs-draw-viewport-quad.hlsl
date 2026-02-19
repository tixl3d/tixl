
static const float3 Quad[] = 
{
  float3(-1, -1, 0),
  float3( 1, -1, 0), 
  float3( 1,  1, 0), 
  float3( 1,  1, 0), 
  float3(-1,  1, 0), 
  float3(-1, -1, 0), 
};


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
    float4 Color;
    float2 Position;
    float Width;
    float Height;
};

Texture2D<float4> InputTexture : register(t0);
sampler texSampler : register(s0);


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};


vsOutput vsMain(uint vertexId: SV_VertexID)
{
    vsOutput output;
    float2 quadVertex = Quad[vertexId].xy;
    float2 quadVertexInObject = quadVertex * float2(Width, Height);
    //output.position = mul(float4(quadVertexInObject, 0, 1), ObjectToClipSpace);
    output.position = float4(quadVertexInObject.xy +  Position,0,1);

    output.texCoord = quadVertex*float2(0.5, -0.5) + 0.5;

    return output;
}


float4 psMain(vsOutput input) : SV_TARGET
{
    //return float4(1,1,0,1);
    float4 c = InputTexture.SampleLevel(texSampler, input.texCoord,0);
    return clamp(float4(1,1,1,1) * Color * c, 0, float4(1000,1000,1000,1));
}

