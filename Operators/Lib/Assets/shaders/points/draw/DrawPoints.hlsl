#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"

static const float3 Corners[] =
    {
        float3(-1, -1, 0),
        float3(1, -1, 0),
        float3(1, 1, 0),
        float3(1, 1, 0),
        float3(-1, 1, 0),
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

cbuffer Params : register(b1)
{
    float4 Color;

    float PointSize;
    float CutOffTransparent;
    float FadeNearest;
};

cbuffer IntParams : register(b2)
{
    int SegmentCount;
    int ScaleFX;
    int UsePointScale;
};

cbuffer FogParams : register(b3)
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

// For shader graph params
cbuffer Params : register(b4)
{
    /*{FLOAT_PARAMS}*/
}

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
    float fog : FOG;
};

sampler WrappedSampler : register(s0);
sampler ClampedSampler : register(s1);

StructuredBuffer<Point> Points : register(t0);
Texture2D<float4> texture2 : register(t1);

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Additional Resources ==========================================
/*{RESOURCES(t2)}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------
float4 GetField(float4 p)
{
    //p.xyz = mul(float4(p.xyz, 1), WorldToObject).xyz;
    float4 f = 1;
    /*{FIELD_CALL}*/
    return f;
}

float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}
//===================================================================

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;

    int quadIndex = id % 6;
    int particleId = id / 6;
    Point pointDef = Points[particleId];

    // float4 aspect = float4(CameraToClipSpace[1][1] / CameraToClipSpace[0][0],1,1,1);
    float3 quadPos = Corners[quadIndex];
    output.texCoord = float2(0, 1) + (quadPos.xy * 0.5 + 0.5) * float2(1, -1);

    float4 posInObject = float4(pointDef.Position, 1);
    float4 quadPosInCamera = mul(posInObject, ObjectToCamera);
    output.color = Color * Points[particleId].Color;

    // Shrink too close particles
    float4 posInCamera = mul(posInObject, ObjectToCamera);
    float tooCloseFactor = saturate(-posInCamera.z / FadeNearest - 1);
    output.color.a *= tooCloseFactor;

    float sizeFxFactor = ScaleFX == 0
                             ? 1
                         : (ScaleFX == 1) ? pointDef.FX1
                                          : pointDef.FX2;

    float2 s = PointSize * sizeFxFactor * (UsePointScale ? pointDef.Scale.xy : 1);
    quadPosInCamera.xy += quadPos.xy * 0.10 * s;
    output.position = mul(quadPosInCamera, CameraToClipSpace);
    float4 posInWorld = mul(posInObject, ObjectToWorld);

    output.color *= GetField(posInWorld);

    // Fog
    output.fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
    return output;
}

float4 psMain(psInput input) : SV_TARGET
{    
    float4 textureCol = texture2.Sample(WrappedSampler, input.texCoord);

    float4 col = input.color * textureCol;
    if (col.a < CutOffTransparent)
        discard;

    col.rgb = lerp(col.rgb, FogColor.rgb, input.fog * FogColor.a);
    return clamp(col, float4(0, 0, 0, 0), float4(1000, 1000, 1000, 1));
}
