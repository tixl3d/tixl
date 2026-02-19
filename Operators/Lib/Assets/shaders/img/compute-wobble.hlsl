RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer TimeConstants : register(b0)
{
    float globalTime;
    float time;
    float2 dummy;
}

cbuffer ParamConstants : register(b1)
{
    float speed;
    float strength;
    float param3;
    float param4;
}

[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint width, height;
    outputTexture.GetDimensions(width, height);

    float2 uv = (float2)i.xy / float2(width - 1, height - 1);
    uv = uv*2.0 - 1.0;
    float l = length(uv);
    uv *= strength * sin(l*time*speed);
    uv = uv*0.5 + 0.5;
    outputTexture[i.xy] = inputTexture.SampleLevel(texSampler, uv, 0.0);
}
