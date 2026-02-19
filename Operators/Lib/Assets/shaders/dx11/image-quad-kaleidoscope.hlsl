
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
    float Time;
    float WobbleAmount;
    float WobbleFrequency;
    float Noise;
};


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

vsOutput vsMain(uint id: SV_VertexID)
{
    vsOutput output;
    float4 quadPos = float4(Quad[id], 1) ;
    output.texCoord = quadPos.xy*float2(0.5, -0.5) - 0.5;
    output.position = mul(quadPos * float4(1,1,1,1), ObjectToClipSpace);
    return output; 
}


float4 psMain(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord.xy;

    float2  distToCenter = (uv + 0.5) * WobbleFrequency;
    float wobbleFactor =  pow( (sin(Time * 2 * 3.1417 + length(distToCenter)) ) /2, 2);
    float2 wobble = distToCenter * wobbleFactor * WobbleAmount;

    float4 c = inputTexture.Sample(texSampler, uv + wobble);
    return float4(1,1,1,1) * Color * c * (wobbleFactor /2 + 0.5) * Noise;
}
