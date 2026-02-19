#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

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
    float DisplaceAmount;
    float DisplaceOffset;
    float Twist;
    float SampleRadius;

    float3 Center;
}



//StructuredBuffer<LegacyPoint> Points : t0;
RWStructuredBuffer<LegacyPoint> Points : u0;    // output

Texture2D<float4> DisplaceMap : register(t1);
sampler texSampler : register(s0);

[numthreads(256,4,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x; 

    LegacyPoint P = Points[index];
    
    float3 pos = P.Position;
    pos -= Center;
    
    float3 posInObject = mul(float4(pos.xyz,0), WorldToObject).xyz;
    //float3 posInObject = pos.xyz;
  
    float2 uv = posInObject.xy * float2(1,-1) + float2(0.5, 0.5);
    float4 c = DisplaceMap.SampleLevel(texSampler, uv , 0.0);
    float gray = (c.r + c.g + c.b)/3;


    


    float displaceMapWidth, displaceMapHeight;
    DisplaceMap.GetDimensions(displaceMapWidth, displaceMapHeight);

    int dSamples=2;
    float radius2 = 2;
    float sx = SampleRadius / displaceMapWidth;
    float sy = SampleRadius / displaceMapHeight;

    // Points[index].position += float3(0, 0, 0.01 * gray);
    // return;


    float padding = 1;
    float2 d = 0;

    float4 cx1= DisplaceMap.SampleLevel(texSampler, float2(uv.x + sx, uv.y), 0) * padding;
    float x1= (cx1.r + cx1.g + cx1.b) / 3;

    float4 cx2= DisplaceMap.SampleLevel(texSampler, float2(uv.x - sx, uv.y), 0) * padding; 
    float x2= (cx2.r + cx2.g + cx2.b) / 3;

    float4 cy1= DisplaceMap.SampleLevel(texSampler, float2(uv.x,       uv.y + sy), 0)*padding;
    float y1= (cy1.r + cy1.g + cy1.b) / 3;

    float4 cy2= DisplaceMap.SampleLevel(texSampler, float2(uv.x,       uv.y - sy), 0)*padding;    
    float y2= (cy2.r + cy2.g + cy2.b) / 3;

     d += float2( (x1-x2) , (y1-y2));

    d.y *= -1;
    float a = (d.x == 0 && d.y==0) ? 0 :  atan2(d.x, d.y) + Twist / 180 * 3.14158;    
    float2 direction = float2( sin(a), cos(a));
    float len = length(d);

    if(len > 0.0001)
        Points[index].Position += float3(direction * DisplaceAmount/100,0);

}