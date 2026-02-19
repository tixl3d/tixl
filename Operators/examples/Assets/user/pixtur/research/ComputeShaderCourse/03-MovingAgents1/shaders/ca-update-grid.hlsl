#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 DecayRate;
    float2 BlockCount;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct Cell {
    int State;
};

#define mod(x,y) ((x)-(y)*floor((x)/(y)))

Texture2D<float4> FxTexture : register(t0);
sampler texSampler : register(s0);

RWTexture2D<float4> WriteOutput  : register(u0); 
RWStructuredBuffer<LegacyPoint> Points : register(u1); 

// Using a threadcount matching 1920 and 1080

//static const int2 BC = int2(7,7);

[numthreads(30,30,1)]
void main(uint3 i : SV_DispatchThreadID)
{   
    int texWidth;
    int texHeight;
    WriteOutput.GetDimensions(texWidth, texHeight);

    int2 res= float2(texWidth, texHeight) / BlockCount;
    int2 block = (int2)((i.xy + float2(0.5,+0.75)) / res) * res;

    float4 sumNeighbours = 
                (0
                +WriteOutput[mod((i.xy + float2(0,  1)), res) + block]
                +WriteOutput[mod((i.xy + float2(0, -1)), res) + block]
                +WriteOutput[mod((i.xy + float2(-1, 0)), res) + block]
                +WriteOutput[mod((i.xy + float2(+1, 0)), res) + block]
                +WriteOutput[mod(i.xy, res) + block]                
                )/5;

    //WriteOutput[i.xy] = float4(block.xy/1000., 0,1);
    //return;
    
    float2 uv = float2( (float)i.x /texWidth , (float)i.y / texHeight  );
    float4 fx = FxTexture.SampleLevel(texSampler, uv, 0);
    float4 diffused = float4((sumNeighbours * DecayRate).rgb ,1);


    WriteOutput[i.xy] = diffused + float4(fx.rgb,0) * fx.a ;
    //WriteOutput[i.xy] = float4(lerp(diffused.rgb, fx.rgb, fx.a), diffused.a);
}
