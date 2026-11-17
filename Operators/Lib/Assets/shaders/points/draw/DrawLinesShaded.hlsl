#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"
#include "shared/velocity-functions.hlsl"

/** x selects the segment's start or end point, y the side of the strip. */
static const float2 Corners[] =
    {
        float2(0, -1),
        float2(1, -1),
        float2(1, 1),
        float2(1, 1),
        float2(0, 1),
        float2(0, -1),
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

    float Width;
    float CutOffTransparent;
    float FadeNearest;
};

cbuffer IntParams : register(b2)
{
    int WidthFactorMode;
    int UsePointScale;
    int Smoothing;
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

cbuffer FloatParams : register(b6)
{
    /*{FLOAT_PARAMS}*/
}

/** Last frame's transforms of this draw, for the velocity target. */
cbuffer PreviousTransforms : register(b7)
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

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
    float fog : FOG;
    float3 axisPointInCamera : POSITION2;
    float3 axisDirectionInCamera : DIRECTION;
    float3 quadPosInCamera : POSITION3;
    float3 movementInObject : MOTION;
    float radius : RADIUS;
};

struct psOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Velocity : SV_Target2;

    // The tube's front lies between the strip and the camera, so depth only ever moves closer.
    float Depth : SV_DepthLessEqual;
};

sampler WrappedSampler : register(s0);
sampler ClampedSampler : register(s1);

#define LinearSampler WrappedSampler

StructuredBuffer<Point> Points : register(t0);

Texture2D<float4> BaseColorMap : register(t1);
Texture2D<float4> EmissiveColorMap : register(t2);
Texture2D<float4> RSMOMap : register(t3);
Texture2D<float4> NormalMap : register(t4);

TextureCube<float4> PrefilteredSpecular : register(t5);
Texture2D<float4> BRDFLookup : register(t6);

/** The points one frame earlier, in the same order. Unbound or shorter, points fall back to their current position. */
StructuredBuffer<Point> PreviousPoints : register(t7);

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Additional Resources ==========================================
/*{RESOURCES(t8)}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------

//-------------------------------------------------------------------
inline float4 GetField(float4 p)
{
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

/** Smoothing beyond this many neighbours per side costs more than it changes the result. */
static const int MaxSmoothing = 32;

/** The smoothed position, direction and movement of a line point, built from its neighbours within the same line. */
struct LineFrame
{
    float3 Position;
    float3 Direction;
    float3 Movement;
};

LineFrame GetSmoothedFrame(int pointIndex, int pointCount, bool hasPrevious)
{
    int smoothing = clamp(Smoothing, 0, MaxSmoothing);
    int reach = max(smoothing, 1);

    // Neighbours count only up to the next separator or buffer end, so lines never blend into each other.
    // HLSL does not short-circuit '&&', so each index is range-checked before it is read.
    int pointsBefore = 0;
    for (; pointsBefore < reach; pointsBefore++)
    {
        int neighbourIndex = pointIndex - pointsBefore - 1;
        if (neighbourIndex < 0)
            break;

        if (IsSeparator(Points[neighbourIndex]))
            break;
    }

    int pointsAfter = 0;
    for (; pointsAfter < reach; pointsAfter++)
    {
        int neighbourIndex = pointIndex + pointsAfter + 1;
        if (neighbourIndex >= pointCount)
            break;

        if (IsSeparator(Points[neighbourIndex]))
            break;
    }

    // A symmetric window keeps the line's end points in place; it narrows towards them.
    int positionWindow = min(smoothing, min(pointsBefore, pointsAfter));
    int directionWindow = max(positionWindow, 1);

    float3 positionSum = 0;
    float3 movementSum = 0;
    float weightSum = 0;
    float3 direction = 0;

    for (int offset = -directionWindow; offset <= directionWindow; offset++)
    {
        int neighbourIndex = pointIndex + clamp(offset, -pointsBefore, pointsAfter);
        float3 position = Points[neighbourIndex].Position;

        // Triangle weights: smoother than a box filter at the same cost.
        float weight = positionWindow + 1 - abs(offset);
        if (abs(offset) <= positionWindow)
        {
            positionSum += position * weight;
            weightSum += weight;
            if (hasPrevious)
                movementSum += (position - PreviousPoints[neighbourIndex].Position) * weight;
        }

        direction += position * sign(offset) * (directionWindow + 1 - abs(offset));
    }

    LineFrame frame;
    frame.Position = positionSum / weightSum;
    frame.Movement = movementSum / weightSum;
    frame.Direction = direction;
    return frame;
}

psInput vsMain(uint id : SV_VertexID)
{
    psInput output = (psInput)0;

    uint pointCount, pointStride;
    Points.GetDimensions(pointCount, pointStride);

    int cornerIndex = id % 6;
    int segmentIndex = id / 6;
    float2 corner = Corners[cornerIndex];

    // The draw covers one quad per point, so the last one has no segment. Separators break the line.
    bool hasSegment = segmentIndex + 1 < (int)pointCount;
    if (hasSegment)
        hasSegment = !IsSeparator(Points[segmentIndex]) && !IsSeparator(Points[segmentIndex + 1]);

    if (!hasSegment)
    {
        output.position = 0;
        return output;
    }

    int pointIndex = segmentIndex + (int)corner.x;
    Point pointDef = Points[pointIndex];

    // An unbound or shorter buffer reads zeros; a zero rotation marks such a point.
    uint previousCount, previousStride;
    PreviousPoints.GetDimensions(previousCount, previousStride);
    bool hasPrevious = false;
    if (previousCount == pointCount)
    {
        float4 previousRotation = PreviousPoints[pointIndex].Rotation;
        hasPrevious = dot(previousRotation, previousRotation) > 1e-6;
    }

    LineFrame frame = GetSmoothedFrame(pointIndex, (int)pointCount, hasPrevious);
    output.movementInObject = frame.Movement;

    float sizeFactor = WidthFactorMode == 0   ? 1
                       : WidthFactorMode == 1 ? pointDef.FX1
                                              : pointDef.FX2;
    float radiusInObject = Width * 0.5 * sizeFactor * (UsePointScale != 0 ? pointDef.Scale.x : 1);

    float3 axisPointInCamera = mul(float4(frame.Position, 1), ObjectToCamera).xyz;
    float3 axisDirectionInCamera = mul(float4(frame.Direction, 0), ObjectToCamera).xyz;
    axisDirectionInCamera = dot(axisDirectionInCamera, axisDirectionInCamera) > 1e-12 ? normalize(axisDirectionInCamera) : float3(1, 0, 0);

    // The strip faces the eye around the line's axis, so it covers the tube's silhouette from every angle.
    bool isOrthographic = abs(CameraToClipSpace._34) < 1e-6;
    float3 towardsEye = isOrthographic ? float3(0, 0, 1) : -axisPointInCamera;
    float3 side = cross(axisDirectionInCamera, towardsEye);
    if (dot(side, side) < 1e-12)
        side = cross(axisDirectionInCamera, abs(axisDirectionInCamera.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0));
    side = normalize(side);

    // Object scale applies to the width like to the positions.
    float radius = radiusInObject * length(mul(float4(1, 0, 0, 0), ObjectToCamera).xyz);
    output.radius = radius;

    // Seen in perspective, the tube's silhouette lies closer to the eye than its axis and so projects wider.
    float distanceToEye = length(towardsEye - dot(towardsEye, axisDirectionInCamera) * axisDirectionInCamera);
    float halfWidth = isOrthographic
                          ? radius
                          : radius * distanceToEye / sqrt(max(distanceToEye * distanceToEye - radius * radius, radius * radius * 0.01));

    float3 quadPosInCamera = axisPointInCamera + side * corner.y * halfWidth;
    output.axisPointInCamera = axisPointInCamera;
    output.axisDirectionInCamera = axisDirectionInCamera;
    output.quadPosInCamera = quadPosInCamera;
    output.position = mul(float4(quadPosInCamera, 1), CameraToClipSpace);

    output.texCoord = float2((float)pointIndex / max((float)pointCount - 1, 1), corner.y * 0.5 + 0.5);
    output.color = pointDef.Color * Color;

    // Fade lines that come too close to the camera.
    float tooCloseFactor = saturate(-axisPointInCamera.z / FadeNearest - 1);
    output.color.a *= tooCloseFactor;

    output.fog = pow(saturate(-axisPointInCamera.z / FogDistance), FogBias);
    return output;
}

psOutput psMain(psInput pin)
{
    psOutput output;

    // Ray from the eye (orthographic: along the view axis) through this pixel, intersected with the tube's cylinder.
    bool isOrthographic = abs(CameraToClipSpace._34) < 1e-6;
    float3 rayDirection = isOrthographic ? float3(0, 0, -1) : normalize(pin.quadPosInCamera);
    float3 rayOrigin = isOrthographic ? pin.quadPosInCamera : 0;

    float3 axis = normalize(pin.axisDirectionInCamera);
    float3 axisToOrigin = rayOrigin - pin.axisPointInCamera;
    float3 rayAcross = rayDirection - dot(rayDirection, axis) * axis;
    float3 originAcross = axisToOrigin - dot(axisToOrigin, axis) * axis;

    float a = max(dot(rayAcross, rayAcross), 1e-8);
    float b = dot(originAcross, rayAcross);
    float c = dot(originAcross, originAcross) - pin.radius * pin.radius;

    // The strip's outer edge grazes the silhouette, so rounding can push it just past; clamping keeps it closed.
    float h = max(b * b - a * c, 0);
    float3 surfaceInCamera = rayOrigin + rayDirection * ((-b - sqrt(h)) / a);

    float3 axisToSurface = surfaceInCamera - pin.axisPointInCamera;
    float3 normalInCamera = axisToSurface - dot(axisToSurface, axis) * axis;
    normalInCamera = dot(normalInCamera, normalInCamera) > 1e-12 ? normalize(normalInCamera) : -rayDirection;

    float4 roughnessMetallicOcclusion = RSMOMap.Sample(WrappedSampler, pin.texCoord);
    frag.Roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    frag.Metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    frag.Occlusion = roughnessMetallicOcclusion.z;
    frag.albedo = pin.color;

    frag.N = normalize(mul(float4(normalInCamera, 0), CameraToWorld).xyz);
    frag.worldPosition = mul(float4(surfaceInCamera, 1), CameraToWorld).xyz;

    float4 surfaceInClipSpace = mul(float4(surfaceInCamera, 1), CameraToClipSpace);
    output.Depth = surfaceInClipSpace.z / surfaceInClipSpace.w;

    float3 surfaceInObject = mul(float4(frag.worldPosition, 1), WorldToObject).xyz;
    float4 previousClipPosition = mul(float4(surfaceInObject - pin.movementInObject, 1), PrevObjectToClipSpace);
    output.Velocity = EncodeVelocity(surfaceInClipSpace, previousClipPosition, CameraToClipSpace, PrevCameraToClipSpace);

    frag.uv = pin.texCoord;
    frag.fog = pin.fog;

    float3 eyePosition = mul(float4(0, 0, 0, 1), CameraToWorld).xyz;
    frag.Lo = normalize(eyePosition - frag.worldPosition);

    float4 litColor = ComputePbr();
    litColor.rgba *= GetField(float4(frag.worldPosition.xyz, 0)).rgba;
    if (litColor.a < 0.04)
        discard;

    output.Color = litColor;
    output.Normal = float4(frag.N, 1);
    return output;
}
