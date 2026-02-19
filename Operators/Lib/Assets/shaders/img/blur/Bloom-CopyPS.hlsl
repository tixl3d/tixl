// Samples the input texture and outputs the color directly.

Texture2D SourceTexture : register(t0);
SamplerState PointSampler : register(s0);

struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float2 uv  : TEXCOORD0;
};

float4 psMain(PS_INPUT input) : SV_Target
{
    return SourceTexture.Sample(PointSampler, input.uv);
}
    