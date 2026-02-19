cbuffer ParamConstants : register(b0)
{
    float4 Color;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

float4 psMain(vsOutput input) : SV_TARGET
{
    float4 f = inputTexture.Sample(texSampler, input.texCoord);
    return float4(1,1,1,1) * Color *f;
}
