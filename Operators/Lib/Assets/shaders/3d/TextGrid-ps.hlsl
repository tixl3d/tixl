cbuffer ParamConstants : register(b1)
{
    float4 Color;
}

struct Output
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float4 color : COLOR;
};

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

float4 psMain(Output input) : SV_TARGET
{
    float4 texColor = inputTexture.Sample(texSampler, input.texCoord);
    return texColor * input.color;
}
