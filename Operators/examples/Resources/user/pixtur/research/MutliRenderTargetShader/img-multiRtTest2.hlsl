cbuffer ParamConstants : register(b0)
{
    float Time;
}


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
sampler texSampler : register(s0);

struct Output
{
    float4 color : SV_Target;
    float4 more1 : SV_TARGET1;
    float4 more2 : SV_TARGET2;
};

float4 TestGradient(float2 uv, float2 offset, float3 color) 
{
    float l = saturate(1-length(uv - 0.5 -
                float2( sin(Time) *offset.x,
                        cos(Time) *offset.y)) * 2 );

    return float4(color* l, 1); 
}

Output psMain(vsOutput input)  
{
    Output output;
    output.color = TestGradient(input.uv, 0.1, float3(1,0,0));
    output.more1 = TestGradient(input.uv, -0.1, float3(0,1,0));
    output.more2 = TestGradient(input.uv, float2(0.1, 0.5), float3(0,0,1))  ;

    return output;
}