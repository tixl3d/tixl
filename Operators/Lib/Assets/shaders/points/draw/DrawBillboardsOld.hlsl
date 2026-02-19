#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/hash-functions.hlsl"

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

    float2 Stretch;
    float Scale;
    float ScaleRandomly;

    float WMappingScale;
    float Rotate;
    float RotateRandomly;
    float UsePointOrientation;

    float AlphaCutOff;
};

cbuffer FogParams : register(b2)
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
    float fog : FOG;
};

sampler texSampler : register(s0);

StructuredBuffer<LegacyPoint> Points : t0;
Texture2D<float4> SpriteTexture : register(t1);
Texture2D<float4> ColorOverW : register(t2);
Texture2D<float> SizeOverW : register(t3);

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;
    float discardFactor = 1;
    int quadIndex = id % 6;
    int particleId = id / 6;
    float3 cornerFactors = Corners[quadIndex];

    LegacyPoint p = Points[particleId];

    float2 scatter = hash21(particleId) * 2 - 1;

    // float4 aspect = float4(CameraToClipSpace[1][1] / CameraToClipSpace[0][0],1,1,1);
    float3 quadPos = Corners[quadIndex];
    output.texCoord = (quadPos.xy * 0.5 + 0.5);

    // float4 pRotation = ApplyPointOrientaiton > 0.5 ? p.rotation : float4(0,0,0,1);
    // float4 rotation = qMul(pRotation, qFromAngleAxis(Rotate/180*PI, RotateAxis));

    float4 posInObject = float4(p.Position, 1);
    // float3 axis = qRotateVec3(p.position, rotation) * Size * sizeFromW;

    float4 quadPosInCamera = mul(posInObject, ObjectToCamera);
    output.color = Color;

    // Shrink too close particles
    float4 posInCamera = mul(posInObject, ObjectToCamera);
    float tooCloseFactor = saturate(-posInCamera.z / 0.1 - 1);

    float sizeFromW = SizeOverW.SampleLevel(texSampler, float2(p.W * WMappingScale, 0), 0);
    float3 corner = float3(quadPos.xy * 0.010 * Stretch, 0) * float3(1, -1, 1);

    float4 rot = qFromAngleAxis((Rotate + RotateRandomly * scatter.x) * 3.141578 / 180, float3(0, 0, 1));

    if ((int)UsePointOrientation == 1)
    {
        rot = qMul(rot, p.Rotation);
    }
    corner = qRotateVec3(corner, rot);
    // corner =  ApplyPointOrientaiton > 0.5 ? qRotateVec3(corner, p.rotation )
    //                                     : corner; // flipping rotation to match default radial billboards

    float hideUndefinedPoints = isnan(p.W) ? 0 : 1;
    quadPosInCamera.xy += corner * Scale * (ScaleRandomly * scatter.y + 1) * tooCloseFactor * sizeFromW * hideUndefinedPoints;

    output.position = mul(quadPosInCamera, CameraToClipSpace);
    float4 posInWorld = mul(posInObject, ObjectToWorld);

    // float3 axis = cornerFactors;
    // axis.xy = (axis.xy + Offset) * Stretch;
    // axis.z = 0;

    // float3 pInObject = p.position + axis;
    // output.position  = mul(float4(pInObject,1), ObjectToClipSpace);
    // output.texCoord = cornerFactors.xy /2 +0.5;
    float4 colorFromPoint = ((int)UsePointOrientation == 2) ? p.Rotation : 1;
    output.color = Color * ColorOverW.SampleLevel(texSampler, float2(p.W * WMappingScale, 0), 0) * colorFromPoint;

    // Fog
    // if(FogDistance > 0)
    // {
    //     float4 posInCamera = mul(posInObject, ObjectToCamera);
    //     float fog = pow(saturate(-posInCamera.z/FogDistance), FogBias);
    //     output.fog = fog;
    // }

    output.fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
    return output;
}

float4 psMain(psInput input) : SV_TARGET
{
    float4 imgColor = SpriteTexture.Sample(texSampler, input.texCoord);
    float4 color = input.color * imgColor;
    if (color.a < AlphaCutOff)
        discard;

    color.rgb = lerp(color.rgb, FogColor.rgb, input.fog);
    return clamp(color, 0, float4(100, 100, 100, 1));
}
