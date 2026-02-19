
static const float3 Quad[] =
    {
        float3(-1, -1, 0),
        float3(1, -1, 0),
        float3(1, 1, 0),
        float3(1, 1, 0),
        float3(-1, 1, 0),
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

Texture2D<float4> InputTexture : register(t0);
sampler texSampler : register(s0);

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

// struct vsOutputOnlyColor
// {
//     float4 position : SV_POSITION;
//     float4 color : COLOR0;
// };

vsOutput vsMain(uint vertexId : SV_VertexID)
{
    vsOutput output;
    float2 quadVertex = Quad[vertexId].xy;
    float2 quadVertexInObject = quadVertex * float2(Width, Height);
    output.position = mul(float4(quadVertexInObject, 0, 1), ObjectToClipSpace);
    output.texCoord = quadVertex * float2(0.5, -0.5) + 0.5;

    return output;
}

// vsOutput vsMainOnlyColor(uint vertexId: SV_VertexID)
// {
//     vsOutputOnlyColor output;
//     float4 quadPos = float4(Quad[vertexId], 1) ;
//     float4 size = float4(Width,Height,1,1);
//     output.position = mul(clipSpaceTobject, float4(Quad[vertexId]*1,1) * size);
//     output.color = Color;

//     return output;
// }

float4 psMain(vsOutput input) : SV_TARGET
{
    float4 c = InputTexture.Sample(texSampler, input.texCoord);
    return clamp(float4(1, 1, 1, 1) * Color * c, 0, float4(1000, 1000, 1000, 1));
}

float4 psMainOnlyColor(vsOutput input) : SV_TARGET
{
    return float4(1, 1, 0, 1); // saturate(Color);
}
