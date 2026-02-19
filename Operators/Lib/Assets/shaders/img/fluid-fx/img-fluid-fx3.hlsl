cbuffer ParamConstants : register(b0)
{
    float Size;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> InputA : register(t0);
Texture2D<float4> InputB : register(t1);
sampler samLinear : register(s0);


float4 psMain3(vsOutput input) : SV_TARGET
{
    //return float4(1,1,0,1);
    float2 uv = input.texCoord;
    float4 a = InputA.Sample(samLinear,uv);
    return float4(a.rrr * 1 + 0.5,1);

    return a.z*(+sin(a.x*4.+float4(1,3,5,4))*.2
                     +sin(a.y*4.+float4(1,3,2,4))*.2+.6);
}

