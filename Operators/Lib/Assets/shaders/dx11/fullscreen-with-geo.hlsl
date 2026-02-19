
struct Output
{
    float4 position : SV_POSITION;
};

Output vsMain(float4 position : POSITION)
{
    Output output;

    output.position = position;

    return output;
}
