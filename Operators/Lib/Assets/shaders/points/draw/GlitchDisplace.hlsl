#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/hash-functions.hlsl"

static const float3 Corners[] = 
{
  float3(-1, -1, 0),
  float3( 1, -1, 0), 
  float3( 1,  1, 0), 
  float3( 1,  1, 0), 
  float3(-1,  1, 0), 
  float3(-1, -1, 0), 
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

cbuffer Params : register(b2)
{
    float2 ScatterPosition;
    float2 Stretch;

    float2 ScatterStretch;
    float2 Displace;

    float2 ScatterDisplace;
    float Size;
    float ColorRatio;

    float4 Colorize;

    float Seed;
    float Mode;
    float Threshold;
    float Amount;
};

static const float ModeStatic = 0+0.5;
static const float ModeHighlights = 1+0.5;
static const float ModeShadows = 2+0.5;
static const float ModeEdgeLeft = 3+0.5;
static const float ModeEdgeRight = 4+0.5;

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
};

sampler texSampler : register(s0);

StructuredBuffer<LegacyPoint> Points : t0;
Texture2D<float4> texture2 : register(t1);

psInput vsMain(uint id: SV_VertexID)
{
    psInput output;

    int quadIndex = id % 6;
    int particleId = id / 6;
    LegacyPoint pointDef = Points[particleId];

    float4 aspect = float4(CameraToClipSpace[1][1] / CameraToClipSpace[0][0],1,1,1);
    float3 cornerDef = Corners[quadIndex];
    output.texCoord = (cornerDef.xy * 0.5 + 0.5);

    float2 rand = hash21((particleId + Seed * 1771) % 33533);
    float2 randCentered = (rand - 0.5) * 2;
    float2 rand2Centered = hash21((particleId + Seed * 2701) % 33533);

    float4 centerInObject = float4(pointDef.Position,1) + float4(randCentered * ScatterPosition,0,0);
    centerInObject.x *= aspect;
    float4 centerInCamera = mul(centerInObject, ObjectToCamera);
    centerInCamera.xyz /= centerInCamera.w;

    float4 centerInClipspace= mul(centerInCamera, CameraToClipSpace);
    float2 centerUv =   (centerInClipspace.xy / centerInClipspace.w + 1) * 0.5;
    centerUv.y = 1- centerUv.y;

    float2 quadSize = pointDef.W * Size * Stretch * (1 + pow( rand, 2) * ScatterStretch) * 0.05;
    quadSize.x *= aspect;
    float4 vertexInClipSpace = centerInClipspace 
                             + float4(cornerDef.xy * quadSize, 0,0);
    
    
    float2 vertexUv =   (vertexInClipSpace.xy / vertexInClipSpace.w + 1) * 0.5;
    vertexUv.y = 1 - vertexUv.y;
    
    float4 sourceColor1 = texture2.SampleLevel(texSampler, centerUv,0);
    float4 sourceColor2 = texture2.SampleLevel(texSampler, centerUv+ float2(0.01,0),0);

    float brightness1 = (sourceColor1.r + sourceColor1.g + sourceColor1.b) / 3 * sourceColor1.a;
    float brightness2 = (sourceColor2.r + sourceColor2.g + sourceColor2.b) / 3 * sourceColor2.a;

    float displaceFactor = 0; ;// saturate(sourceColor.r - sourceColor2.r) * 10;
    if(Mode < ModeStatic) {

    }
    else if(Mode < ModeHighlights) {
        displaceFactor = saturate(brightness1 - Threshold);
    }
    else if(Mode < ModeShadows) {
        displaceFactor = saturate(1-brightness1 - Threshold);
    }
    else if(Mode < ModeEdgeLeft) {
        displaceFactor = saturate(brightness2 - brightness1 - Threshold) * 3;
    }
    else if(Mode < ModeEdgeRight) {
        displaceFactor = saturate(brightness1 - brightness2 - Threshold) * 3;
    }
    displaceFactor *= Amount;

    output.position =  vertexInClipSpace;
    if( abs(displaceFactor) < 0.0001 ) {
        output.position =  0;
    }

    output.texCoord = vertexUv + (Displace/100 + ScatterDisplace/100) * displaceFactor;

    // Highlight     
    output.color =  (ColorRatio > rand.x) ? Colorize : 0;
    output.color.a *= displaceFactor;
    return output;
}

float4 psMain(psInput input) : SV_TARGET
{    
    //return input.color;
    //return float4(input.texCoord,0,1);
    float4 textureCol = texture2.Sample(texSampler, input.texCoord);

    //return float4(input.color.rgba);
    return lerp( textureCol, float4(input.color.rgb,1), input.color.a);

    // if(textureCol.a < CutOffTransparent)
    //     discard;

    // float4 col = input.color * textureCol;
    // col.rgb = lerp(col.rgb, FogColor.rgb, input.fog);
    // return clamp(col, float4(0,0,0,0), float4(1000,1000,1000,1));
}
