#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/hash-functions.hlsl"

static const float4 Factors[] = 
{
  //     x  y  z  w
  float4(0, 0, 0, 0), // 0 nothing
  float4(1, 0, 0, 0), // 1 for x
  float4(0, 1, 0, 0), // 2 for y
  float4(0, 0, 1, 0), // 3 for z
  float4(0, 0, 0, 1), // 4 for w
  float4(0, 0, 0, 0), // avoid rotation effects
};

cbuffer Transforms : register(b0)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

cbuffer Params : register(b1)
{
    float L;
    float LFactor;
    float LOffset;

    float R;
    float RFactor;
    float ROffset;

    float G;
    float GFactor;
    float GOffset;

    float B;
    float BFactor;
    float BOffset;

    // float A;
    // float AFactor;
    // float AOffset;

    float3 Center;
    float __padding;

    float Phase;
    float Frequency;
    float Amount;
    float Variation;

    float UseRemapCurve;
}


float3 GetNoise(float3 pos, float3 variation) 
{
    float3 noiseLookup = (pos * 0.91 + variation + Phase ) * Frequency;
    return snoiseVec3(noiseLookup);
}

StructuredBuffer<LegacyPoint> Points : t0;
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;    // output


Texture2D<float4> remapCurveTexture : register(t1);
sampler texSampler : register(s0);

[numthreads(256,4,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x; 

    LegacyPoint P = Points[index];
    float3 pos = P.Position;
    pos -= Center;
    
    //float3 posInObject = mul(float4(pos.xyz,0), WorldToObject).xyz;
  

    float3 variationOffset = hash31((float)(i.x%1234)/0.123 ) * Variation;
    float3 c = GetNoise(P.Position + Center, variationOffset);
    if(UseRemapCurve > 0.5) 
    {
        //c *= 0.200;
        c.r = (remapCurveTexture.SampleLevel(texSampler, float2(c.r/2+0.5, 0.5) , 0).r*2-1)  * Amount / 100;
        c.g = (remapCurveTexture.SampleLevel(texSampler, float2(c.g/2+0.5, 0.5) , 0).g*2-1) * Amount / 100;
        c.b = (remapCurveTexture.SampleLevel(texSampler, float2(c.b/2+0.5, 0.5) , 0).b*2-1) * Amount / 100;
    }
    else {
        c *= Amount / 100;
    }
    //c*= remapCurveTexture.SampleLevel(texSampler, float2(0.5,0.5) , 0);

    float gray = (c.r + c.g + c.b)/3;

    float4 ff =
              Factors[(uint)clamp(L, 0, 5.1)] * (gray * LFactor + LOffset) 
            + Factors[(uint)clamp(R, 0, 5.1)] * (c.r * RFactor + ROffset)
            + Factors[(uint)clamp(G, 0, 5.1)] * (c.g * GFactor + GOffset)
            + Factors[(uint)clamp(B, 0, 5.1)] * (c.b * BFactor + BOffset);

    P.Position += float3(ff.xyz);
    P.W = clamp(P.W + ff.w,0, 10000);
    
    
    float4 rot = P.Rotation;
    ResultPoints[index].Rotation = P.Rotation;

    float rotXFactor = (R == 5 ? (c.r * RFactor + ROffset) : 0)
                     + (G == 5 ? (c.g * GFactor + GOffset) : 0)
                     + (B == 5 ? (c.b * BFactor + BOffset) : 0)
                     + (L == 5 ? (gray * LFactor + LOffset) : 0);

    float rotYFactor = (R == 6 ? (c.r * RFactor + ROffset) : 0)
                     + (G == 6 ? (c.g * GFactor + GOffset) : 0)
                     + (B == 6 ? (c.b * BFactor + BOffset) : 0)
                     + (L == 6 ? (gray * LFactor + LOffset) : 0);

    float rotZFactor = (R == 7 ? (c.r * RFactor + ROffset) : 0)
                     + (G == 7 ? (c.g * GFactor + GOffset) : 0)
                     + (B == 7 ? (c.b * BFactor + BOffset) : 0)
                     + (L == 7 ? (gray * LFactor + LOffset) : 0);
                     
    if(rotXFactor != 0) { rot = qMul(rot, qFromAngleAxis(rotXFactor, float3(1,0,0))); }
    if(rotYFactor != 0) { rot = qMul(rot, qFromAngleAxis(rotYFactor, float3(0,1,0))); }
    if(rotZFactor != 0) { rot = qMul(rot, qFromAngleAxis(rotZFactor, float3(0,0,1))); }
    P.Rotation = normalize(rot);

    ResultPoints[i.x] = P;
}