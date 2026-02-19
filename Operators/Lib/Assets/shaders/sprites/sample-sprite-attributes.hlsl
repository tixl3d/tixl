#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/SpriteDef.hlsl"

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
    float __padding;

    float3 Center;
}



StructuredBuffer<LegacyPoint> Points : t0;
StructuredBuffer<SpriteDef> InputSprites : t1;
RWStructuredBuffer<SpriteDef> ResultSprites : u0;    // output1
//RWStructuredBuffer<SpriteDef> ResultSprites : u0;     // output2


Texture2D<float4> InputTexture : register(t2);
sampler texSampler : register(s0);

[numthreads(256,4,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x; 

    uint spriteCount, _;
    InputSprites.GetDimensions(spriteCount,_);

    uint texWidth, texHeight;
    InputTexture.GetDimensions(texWidth, texHeight);
    float aspectRatio = (float)texWidth / texHeight;
    
    //if(i.x >= spriteCount)
    //    return;
    
    LegacyPoint P = Points[index];
    float3 pos = P.Position;
    pos -= Center;
    pos.x /= aspectRatio;
    pos/= 2;
    
    float3 posInObject = mul(float4(pos.xyz,0), WorldToObject).xyz;  
    float2 uv = posInObject.xy * float2(1,-1) + float2(0.5, 0.5);
    float4 c = InputTexture.SampleLevel(texSampler, uv, 0.0);
    c.a = 1;
    //c.rgb = pos;

    ResultSprites[i.x].Color = c;
    ResultSprites[i.x].Size = 0.1;
    ResultSprites[i.x].Pivot =0;
    ResultSprites[i.x].UvMin =0;
    ResultSprites[i.x].UvMax =1;
    
}