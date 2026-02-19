#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"

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

cbuffer Params : register(b2)
{
    // int SegmentCount;
    int ScaleFactorMode;
    int UsePointScale;
};

cbuffer FogParams : register(b3)
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

cbuffer PointLights : register(b4)
{
    PointLight Lights[8];
    int ActiveLightCount;
}

cbuffer PbrParams : register(b5)
{
    float4 BaseColor;
    float4 EmissiveColor;
    float Roughness;
    float Specular;
    float Metal;
}

cbuffer Params : register(b6)
{
    /*{FLOAT_PARAMS}*/
}

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
    float fog : FOG;
    float3 centerPosInWorld : POSITION2;
    float radius : RADIUS;

    // float3x3 tbnToWorld : TBASIS;
};

struct psOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
};

sampler WrappedSampler : register(s0);
sampler ClampedSampler : register(s1);

static sampler LinearSampler = WrappedSampler;

StructuredBuffer<Point> Points : t0;

Texture2D<float4> BaseColorMap : register(t1);
Texture2D<float4> EmissiveColorMap : register(t2);
Texture2D<float4> RSMOMap : register(t3);
Texture2D<float4> NormalMap : register(t4);

TextureCube<float4> PrefilteredSpecular : register(t5);
Texture2D<float4> BRDFLookup : register(t6);


//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Additional Resources ==========================================
/*{RESOURCES(t7)}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------

//-------------------------------------------------------------------
inline float4 GetField(float4 p)
{
#ifndef USE_WORLDSPACE
    //p.xyz = mul(float4(p.xyz, 1), WorldToObject).xyz;
#endif
    float4 f = 1;
    /*{FIELD_CALL}*/

    return f;
}

float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}
//===================================================================



#include "shared/pbr-render.hlsl"

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;

    int quadIndex = id % 6;
    int particleId = id / 6;
    Point pointDef = Points[particleId];

    float3 quadPos = Corners[quadIndex];
    output.texCoord = (quadPos.xy * 0.5 + 0.5);

    float4 posInObject = float4(pointDef.Position, 1);
    float4 quadPosInCamera = mul(posInObject, ObjectToCamera);

    uint colorCount, stride;
    output.color = pointDef.Color * Color;

    float3 posInWorld = mul(quadPosInCamera, CameraToWorld).xyz;
    output.centerPosInWorld = posInWorld;

    // Shrink too close particles
    float4 posInCamera = mul(posInObject, ObjectToCamera);
    float tooCloseFactor = saturate(-posInCamera.z / FadeNearest - 1);
    output.color.a *= tooCloseFactor;

    // float sizeFactor = UseWForSize > 0.5 ? pointDef.W : 1;
    float sizeFactor = ScaleFactorMode == 0
                           ? 1
                       : (ScaleFactorMode == 1) ? pointDef.FX1
                                                : pointDef.FX2;

    float2 s = PointSize * sizeFactor * (UsePointScale ? pointDef.Scale.xy : 1);
    output.radius = s;
    quadPosInCamera.xy += quadPos.xy * 0.050 * s; // sizeFactor * Size * tooCloseFactor;
    output.position = mul(quadPosInCamera, CameraToClipSpace);

    // Fog
    output.fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
    return output;
}

static float3 LightPosition = float3(1, 2, 0);

psOutput psMain(psInput pin) : SV_TARGET
{
    psOutput output;
    // Sphere Shading...
    float2 p = pin.texCoord * float2(2.0, 2.0) - float2(1.0, 1.0);
    float d = dot(p, p);
    if (d > 0.93)
        discard;

    // Sample input textures to get shading model params.
    float4 albedo = pin.color;
    
    float4 roughnessMetallicOcclusion = RSMOMap.Sample(WrappedSampler, pin.texCoord);
    frag.Roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    frag.Metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    frag.Occlusion = roughnessMetallicOcclusion.z;
    frag.albedo = albedo;

    float z = sqrt(1 - d * d);
    float3 normal = normalize(float3(p, z));
    frag.N = mul(float4(normal, 0), CameraToWorld).xyz;

    frag.worldPosition = pin.centerPosInWorld + normal * pin.radius *0.01;
        
    frag.uv = pin.texCoord;
    frag.fog = pin.fog;

    float3 eyePosition = mul(float4(0, 0, 0, 1), CameraToWorld);
    frag.Lo = normalize(eyePosition - frag.worldPosition);

    float4 litColor = ComputePbr();
    litColor.rgba *= GetField(float4(frag.worldPosition.xyz, 0)).rgba;
    if (litColor.a < 0.04)
        discard;

    output.Color = litColor;

    output.Normal = float4 (frag.N,1);

    return output;
}
