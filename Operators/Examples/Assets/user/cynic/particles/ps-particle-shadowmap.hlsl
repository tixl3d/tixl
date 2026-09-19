
struct Input
{
    float4 position : SV_POSITION;
    float4 mask0 : MASK0;
    float4 mask1 : MASK1;
    float4 mask2 : MASK2;
    float4 mask3 : MASK3;
    float2 texCoord : TEXCOORD0;
};

struct Output
{
    float4 rt0 : SV_TARGET0;
    float4 rt1 : SV_TARGET1;
    float4 rt2 : SV_TARGET2;
    float4 rt3 : SV_TARGET3;
};


Output psMain(Input input)
{
    Output output = (Output)0;
    float2 xy = 2.0 * input.texCoord - float2(1,1);
    float r2 = dot(xy, xy);
    float opacity = exp2(-r2 * 5.0) * 1.0245;

    if (any(input.mask0))
        output.rt0 = opacity * input.mask0;
    else if (any(input.mask1))
        output.rt1 = opacity * input.mask1;
    else if (any(input.mask2))
        output.rt2 = opacity * input.mask2;
    else 
        output.rt3 = opacity * input.mask3;

    return output;
}