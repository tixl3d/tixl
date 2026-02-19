
struct Output
{
    float4 position : SV_POSITION;
    float2 texcoord : TEXCOORD;
};

Output vsMain(uint id: SV_VertexID)
{
    Output output;

    output.texcoord = float2((id << 1) & 2, id & 2);
    output.position = float4(output.texcoord * float2(2, -2) + float2(-1, 1), 0, 1);

    return output;
}


Texture2D    colorTexture : register(t0);
SamplerState samLinear : register(s0);

float4 psMain(Output input) : SV_TARGET
{
    float2 texCoord = input.texcoord;
    //return float4(colorTexture.Sample(samLinear, texCoord).rgb, 1.0);
    return colorTexture.Sample(samLinear, texCoord);
}

