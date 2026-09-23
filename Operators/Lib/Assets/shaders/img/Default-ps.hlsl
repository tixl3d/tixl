cbuffer TimeConstants : register(b0)
{
    float globalTime;
    float time;
    float2 dummy;
}

float4 main(float4 input : SV_POSITION) : SV_TARGET
{
    return float4(1,sin(globalTime)*0.5 + 0.5,1,0.7);
}

