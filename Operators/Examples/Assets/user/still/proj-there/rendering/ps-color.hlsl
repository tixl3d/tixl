
struct Input
{
    float4 position : SV_POSITION;
};

float4 psMain(Input input) : SV_TARGET
{
    float4 color = float4(0.75,0.6,0.4,1);

    return color;
}
