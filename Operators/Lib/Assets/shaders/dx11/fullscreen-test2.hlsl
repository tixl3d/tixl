
struct Output
{
    float4 position : SV_POSITION;
};

Output vsMain(uint id: SV_VertexID)
{
    Output output;

    output.position = float4(float2((id << 1) & 2, id & 2) * float2(1, -1) + float2(-1, 1), 0, 1);

    return output;
}


float4 psMain(Output input) : SV_TARGET
{
    return float4(1,0,1,0.5);
}

