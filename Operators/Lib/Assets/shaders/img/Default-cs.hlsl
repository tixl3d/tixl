RWTexture2D<float4> outputTexture : register(u0);

Texture2D<float4> inputTexture : register(t0);
Texture2D<float4> inputTexture2 : register(t1);
sampler texSampler : register(s0);

cbuffer TimeConstants : register(b0)
{
    float globalTime;
    float time;
    float2 dummy;
}

cbuffer ParamConstants : register(b1)
{
    float param1;
    float param2;
    float param3;
    float param4;
}

[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint width, height;
    outputTexture.GetDimensions(width, height);

    float2 uv = (float2)i.xy / float2(width - 1, height - 1);
    float b = sin(time)*0.5 + 0.5;
    float4 calcColor = float4(uv, b, 1);
    float4 inputColor = inputTexture.SampleLevel(texSampler, uv, 0.0);
    inputColor *= 3*inputTexture2.SampleLevel(texSampler, uv, 0);
    float4 outputColor = lerp(calcColor, inputColor, b);
    outputColor.r = param1;

    outputTexture[i.xy] = outputColor;
}
