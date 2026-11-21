#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/velocity-functions.hlsl"

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
    int SizeMode; // 0: world units, 1: pixels
};

cbuffer FogParams : register(b3)
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

// For shader graph params
cbuffer FloatParams : register(b4)
{
    /*{FLOAT_PARAMS}*/
}

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
    float fog : FOG;
    float4 clipPosition : CLIP_POSITION;
    float4 previousClipPosition : PREVIOUS_CLIP_POSITION;
};

struct psOutput
{
    float4 Color : SV_Target0;

    // Sprites have no surface normal. Zero marks that, and with alpha 0 blended sprites keep the normal behind them.
    float4 Normal : SV_Target1;
    float4 Velocity : SV_Target2;
};

/** Last frame's transforms of this draw, for the velocity target. */
cbuffer Resolution : register(b6)
{
    float TargetWidth;
    float TargetHeight;
};

cbuffer PreviousTransforms : register(b5)
{
    float4x4 PrevCameraToClipSpace;
    float4x4 PrevClipSpaceToCamera;
    float4x4 PrevWorldToCamera;
    float4x4 PrevCameraToWorld;
    float4x4 PrevWorldToClipSpace;
    float4x4 PrevClipSpaceToWorld;
    float4x4 PrevObjectToWorld;
    float4x4 PrevWorldToObject;
    float4x4 PrevObjectToCamera;
    float4x4 PrevObjectToClipSpace;
};

sampler WrappedSampler : register(s0);
sampler ClampedSampler : register(s1);

StructuredBuffer<Point> Points : register(t0);
Texture2D<float4> texture2 : register(t1);

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Additional Resources ==========================================
/** The points one frame earlier, in the same order. Unbound or shorter, points fall back to their current position. */
StructuredBuffer<Point> PreviousPoints : register(t2);

/*{RESOURCES(t3)}*/

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
    float2 cornerOffset = quadPos.xy * 0.10 * s;
    quadPosInCamera.xy += cornerOffset;
    output.position = mul(quadPosInCamera, CameraToClipSpace);

    // In pixels: offset the projected center, so the sprite keeps its size at any distance
    float2 clipPerPixelSize = SizeMode == 1 ? s / max(float2(TargetWidth, TargetHeight), 1) : 0;
    if (SizeMode == 1)
    {
        float4 centerInClip = mul(posInCamera, CameraToClipSpace);
        output.position = centerInClip + float4(quadPos.xy * clipPerPixelSize * centerInClip.w, 0, 0);
    }

    output.clipPosition = output.position;

    // An unbound or shorter buffer reads zeros; a zero rotation marks such a point.
    uint previousCount, previousStride;
    PreviousPoints.GetDimensions(previousCount, previousStride);
    Point previousPoint = pointDef;
    if ((uint)particleId < previousCount)
    {
        Point candidate = PreviousPoints[particleId];
        if (dot(candidate.Rotation, candidate.Rotation) > 1e-6)
            previousPoint = candidate;
    }

    // The sprite keeps facing the camera, so last frame it had the same offset around the previous center.
    float4 previousCenterInCamera = mul(float4(previousPoint.Position, 1), PrevObjectToCamera);
    float4 previousQuadPosInCamera = previousCenterInCamera;
    previousQuadPosInCamera.xy += cornerOffset;
    float4 previousCenterInClip = mul(previousCenterInCamera, PrevCameraToClipSpace);
    float4 previousQuadInClip = SizeMode == 1
                                    ? previousCenterInClip + float4(quadPos.xy * clipPerPixelSize * previousCenterInClip.w, 0, 0)
                                    : mul(previousQuadPosInCamera, PrevCameraToClipSpace);
    output.previousClipPosition = PreviousClipUnlessJumped(previousQuadInClip,
                                                           mul(posInCamera, CameraToClipSpace),
                                                           previousCenterInClip);
    float4 posInWorld = mul(posInObject, ObjectToWorld);

    output.color *= GetField(posInWorld);

    // Fog
    output.fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
    return output;
}

psOutput psMain(psInput input)
{
    psOutput output;

    float4 textureCol = texture2.Sample(WrappedSampler, input.texCoord);

    float4 col = input.color * textureCol;
    if (col.a < CutOffTransparent)
        discard;

    col.rgb = lerp(col.rgb, FogColor.rgb, input.fog * FogColor.a);
    output.Color = clamp(col, float4(0, 0, 0, 0), float4(1000, 1000, 1000, 1));
    output.Normal = 0;
    output.Velocity = EncodeVelocity(input.clipPosition, input.previousClipPosition, CameraToClipSpace, PrevCameraToClipSpace);
    return output;
}
