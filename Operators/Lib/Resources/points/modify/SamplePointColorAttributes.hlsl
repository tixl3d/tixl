#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/blend-functions.hlsl"


cbuffer Params : register(b0)
{
    float4x4 transformSampleSpace;

    float3 Center;
    float Mode;
    float4 BaseColor;
      
}

StructuredBuffer<Point> Points : register(t0);
Texture2D<float4> inputTexture : register(t1);
RWStructuredBuffer<Point> ResultPoints : register(u0); // output


sampler texSampler : register(s0);

[numthreads(256, 4, 1)] void main(uint3 i
                                  : SV_DispatchThreadID)
{
    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount, stride);
    if(i.x >= pointCount) {
        return;
    }

    uint index = i.x;
    

    LegacyPoint p = Points[index];

    float3 pos = p.Position;
    pos -= Center;

    float3 posInObject = mul(float4(pos.xyz, 0), transformSampleSpace).xyz;
    float4 c = inputTexture.SampleLevel(texSampler, posInObject.xy * float2(1.0, -1.0) + float2(0.5, 0.5), 0.0);
    c *=BaseColor;

      
    p.Color = BlendColors(p.Color, c, (int)Mode);
                                         
    ResultPoints[index] = p;
}