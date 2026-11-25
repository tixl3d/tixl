#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"
#include "shared/velocity-functions.hlsl"

/** x selects the segment's start or end point, y the side of the quad. */
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
    float MaxSegmentLength;
};

cbuffer IntParams : register(b2)
{
    int WidthFactorMode;
    int UsePointScale;
    int Smoothing;
    int FlatCaps;
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
    float3 quadPosInCamera : POSITION0;
    nointerpolation float3 startInCamera : POSITION1;
    nointerpolation float3 endInCamera : POSITION2;
    nointerpolation float2 radii : RADIUS;
    nointerpolation float4 startColor : COLOR0;
    nointerpolation float4 endColor : COLOR1;
    nointerpolation float2 lineCoords : TEXCOORD0;
    nointerpolation float3 startMovementInObject : MOTION0;
    nointerpolation float3 endMovementInObject : MOTION1;

    // The smoothed points around the segment, for a curve that turns the shading smoothly through the joints.
    nointerpolation float3 beforeInCamera : POSITION3;
    nointerpolation float3 afterInCamera : POSITION4;

    /** x: the segment continues a line before it, y: after it. */
    nointerpolation float2 hasNeighbours : NEIGHBOURS;
};

struct psOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Velocity : SV_Target2;
    float4 BaseColor : SV_Target3;
    float4 Material : SV_Target4;

    // The quad lies behind the whole segment, so depth only ever moves closer.
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

/** One end of a segment, smoothed with its neighbours within the same line. */
struct LineEnd
{
    float3 PositionInCamera;
    float3 MovementInObject;
    float Radius;
    float4 Color;
    float LineCoord;
};

/** True if the points at i and i + 1 form a segment: both exist and neither is a separator. With a MaxSegmentLength,
    longer jumps break the line the same way, for example where a trail wraps around. */
bool IsSegment(int i, int pointCount)
{
    if (i < 0 || i + 1 >= pointCount)
        return false;

    Point from = Points[i];
    Point to = Points[i + 1];
    if (IsSeparator(from) || IsSeparator(to))
        return false;

    if (MaxSegmentLength <= 0)
        return true;

    float3 step = to.Position - from.Position;
    return dot(step, step) <= MaxSegmentLength * MaxSegmentLength;
}

LineEnd GetLineEnd(int pointIndex, int pointCount, bool previousMatches)
{
    int smoothing = clamp(Smoothing, 0, MaxSmoothing);

    // Neighbours count only up to the next break or buffer end, so lines never blend into each other.
    int pointsBefore = 0;
    for (; pointsBefore < smoothing; pointsBefore++)
    {
        if (!IsSegment(pointIndex - pointsBefore - 1, pointCount))
            break;
    }

    int pointsAfter = 0;
    for (; pointsAfter < smoothing; pointsAfter++)
    {
        if (!IsSegment(pointIndex + pointsAfter, pointCount))
            break;
    }

    // An unbound or shorter buffer reads zeros; a zero rotation marks such a point.
    bool hasPrevious = false;
    if (previousMatches)
    {
        float4 previousRotation = PreviousPoints[pointIndex].Rotation;
        hasPrevious = dot(previousRotation, previousRotation) > 1e-6;
    }

    // A symmetric window keeps the line's end points in place; it narrows towards them.
    int window = min(pointsBefore, pointsAfter);

    float3 positionSum = 0;
    float3 movementSum = 0;
    float weightSum = 0;
    for (int offset = -window; offset <= window; offset++)
    {
        int neighbourIndex = pointIndex + offset;
        float3 position = Points[neighbourIndex].Position;

        // Triangle weights: smoother than a box filter at the same cost.
        float weight = window + 1 - abs(offset);
        positionSum += position * weight;
        weightSum += weight;
        if (hasPrevious)
            movementSum += (position - PreviousPoints[neighbourIndex].Position) * weight;
    }

    Point pointDef = Points[pointIndex];
    float sizeFactor = WidthFactorMode == 0   ? 1
                       : WidthFactorMode == 1 ? pointDef.FX1
                                              : pointDef.FX2;
    float radiusInObject = Width * 0.5 * sizeFactor * (UsePointScale != 0 ? pointDef.Scale.x : 1);

    LineEnd end;
    end.PositionInCamera = mul(float4(positionSum / weightSum, 1), ObjectToCamera).xyz;
    end.MovementInObject = movementSum / weightSum;

    // Object scale applies to the width like to the positions.
    end.Radius = max(radiusInObject * length(mul(float4(1, 0, 0, 0), ObjectToCamera).xyz), 1e-6);
    end.Color = pointDef.Color * Color;
    end.LineCoord = (float)pointIndex / max((float)pointCount - 1, 1);
    return end;
}

/** Ray against the round cone around the segment a-b (spheres of radius ra and rb at its ends and the cone
    tangent to both): the entry distance along the ray and the surface normal. Ignores hits behind the origin. */
bool IntersectRoundCone(float3 rayOrigin, float3 rayDirection, float3 a, float3 b, float ra, float rb,
                        out float distance, out float3 normal)
{
    distance = 0;
    normal = 0;

    float3 ba = b - a;
    float3 oa = rayOrigin - a;
    float3 ob = rayOrigin - b;
    float rr = ra - rb;
    float m0 = dot(ba, ba);
    float m1 = dot(ba, oa);
    float m2 = dot(ba, rayDirection);
    float m3 = dot(rayDirection, oa);
    float m5 = dot(oa, oa);
    float m6 = dot(ob, rayDirection);
    float m7 = dot(ob, ob);

    // Without room for a cone (one sphere holds the other) only the spheres remain.
    float d2 = m0 - rr * rr;
    if (d2 > 1e-12)
    {
        float k2 = d2 - m2 * m2;
        float k1 = d2 * m3 - m1 * m2 + m2 * rr * ra;
        float k0 = d2 * m5 - m1 * m1 + m1 * rr * ra * 2 - m0 * ra * ra;
        float h = k1 * k1 - k0 * k2;
        if (h >= 0 && k2 > 1e-12)
        {
            float t = (-sqrt(h) - k1) / k2;
            float y = m1 - ra * rr + t * m2;
            if (y > 0 && y < d2)
            {
                distance = t;
                normal = normalize(d2 * (oa + t * rayDirection) - ba * y);
                return true;
            }
        }
    }

    float h1 = m3 * m3 - m5 + ra * ra;
    float h2 = m6 * m6 - m7 + rb * rb;
    if (max(h1, h2) < 0)
        return false;

    distance = 1e20;
    if (h1 > 0)
    {
        distance = -m3 - sqrt(h1);
        normal = (oa + distance * rayDirection) / ra;
    }

    if (h2 > 0)
    {
        float t = -m6 - sqrt(h2);
        if (t < distance)
        {
            distance = t;
            normal = (ob + t * rayDirection) / rb;
        }
    }

    return true;
}

psInput vsMain(uint id : SV_VertexID)
{
    psInput output = (psInput)0;

    uint pointCount, pointStride;
    Points.GetDimensions(pointCount, pointStride);

    int cornerIndex = id % 6;
    int segmentIndex = id / 6;
    float2 corner = Corners[cornerIndex];

    // The draw covers one quad per point, so the last one has no segment. Separators and long jumps break the line.
    bool hasSegment = IsSegment(segmentIndex, (int)pointCount);

    if (!hasSegment)
    {
        output.position = 0;
        return output;
    }

    uint previousCount, previousStride;
    PreviousPoints.GetDimensions(previousCount, previousStride);
    bool previousMatches = previousCount == pointCount;

    LineEnd start = GetLineEnd(segmentIndex, (int)pointCount, previousMatches);
    LineEnd end = GetLineEnd(segmentIndex + 1, (int)pointCount, previousMatches);

    float3 a = start.PositionInCamera;
    float3 b = end.PositionInCamera;

    // At line ends the mirrored end point keeps the curve straight there, like the segment itself.
    bool hasBefore = IsSegment(segmentIndex - 1, (int)pointCount);
    bool hasAfter = IsSegment(segmentIndex + 1, (int)pointCount);

    output.hasNeighbours = float2(hasBefore ? 1 : 0, hasAfter ? 1 : 0);
    output.beforeInCamera = hasBefore ? GetLineEnd(segmentIndex - 1, (int)pointCount, false).PositionInCamera : 2 * a - b;
    output.afterInCamera = hasAfter ? GetLineEnd(segmentIndex + 2, (int)pointCount, false).PositionInCamera : 2 * b - a;
    float ra = start.Radius;
    float rb = end.Radius;
    float3 axis = b - a;

    // The quad bounds the segment's round cone as the eye sees it: 'side' is normal to the plane through
    // the eye and the axis, 'along' runs with the axis across the screen. Each end gets its own frame
    // facing the eye, so a segment seen end-on still covers the disc of its nearer cap.
    bool isOrthographic = abs(CameraToClipSpace._34) < 1e-6;
    float3 viewA = isOrthographic ? float3(0, 0, -1) : normalize(a);
    float3 viewB = isOrthographic ? float3(0, 0, -1) : normalize(b);
    float3 side = cross(axis, viewA);
    if (dot(side, side) < 1e-12 * max(dot(axis, axis), 1e-12))
        side = cross(viewA, abs(viewA.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0));
    side = normalize(side);

    float3 alongA = cross(viewA, side);
    float3 alongB = cross(viewB, side);

    float3 quadPosInCamera;
    if (isOrthographic)
    {
        float lengthAlong = dot(axis, alongA);
        float startOffset = min(-ra, lengthAlong - rb);
        float endOffset = max(ra - lengthAlong, rb);
        float halfWidth = max(ra, rb);
        quadPosInCamera = corner.x < 0.5
                              ? a + alongA * startOffset
                              : b + alongB * endOffset;
        quadPosInCamera += side * corner.y * halfWidth;
    }
    else
    {
        // Angles seen from the eye: the radius of each end's disc and the span between the ends.
        float distanceA = length(a);
        float distanceB = length(b);
        float discAngleA = asin(min(ra / distanceA, 0.99));
        float discAngleB = asin(min(rb / distanceB, 0.99));
        float spanAngle = atan2(dot(viewB, alongA), dot(viewB, viewA));

        bool isStart = corner.x < 0.5;
        float edgeAngle = isStart
                              ? min(-discAngleA, spanAngle - discAngleB)
                              : max(discAngleB, discAngleA - spanAngle);
        float3 view = isStart ? viewA : viewB;
        float3 along = isStart ? alongA : alongB;
        float distanceBehind = isStart ? distanceA + ra : distanceB + rb;

        // A straight edge bulges towards the middle, so its corners at the widest disc angle bound both discs.
        float sideOffset = distanceBehind * tan(max(discAngleA, discAngleB)) / cos(edgeAngle);
        quadPosInCamera = distanceBehind * (view + along * tan(edgeAngle)) + side * corner.y * sideOffset;
    }

    // Sliding the corners along their eye rays onto a plane behind the whole segment keeps the
    // covered pixels and makes the rasterized depth a safe bound for SV_DepthLessEqual.
    float backDepth = min(a.z - ra, b.z - rb);
    if (isOrthographic)
    {
        quadPosInCamera.z = backDepth;
    }
    else if (quadPosInCamera.z < -1e-6)
    {
        quadPosInCamera *= backDepth / quadPosInCamera.z;
    }

    output.position = mul(float4(quadPosInCamera, 1), CameraToClipSpace);
    output.quadPosInCamera = quadPosInCamera;
    output.startInCamera = a;
    output.endInCamera = b;
    output.radii = float2(ra, rb);
    output.startColor = start.Color;
    output.endColor = end.Color;
    output.lineCoords = float2(start.LineCoord, end.LineCoord);
    output.startMovementInObject = start.MovementInObject;
    output.endMovementInObject = end.MovementInObject;
    return output;
}

/**
 * First hit of a ray with a segment's cone (radius ra at a to rb at b) cut by two planes through a and b with normals
 * along startCut and endCut. A hit on a cut plane returns cutSide -1 (start) or 1 (end) and the plane as normal.
 */
bool IntersectCutCone(float3 rayOrigin, float3 rayDirection, float3 a, float3 b, float ra, float rb,
                      float3 startCut, float3 endCut, out float distance, out float3 normal, out int cutSide)
{
    distance = 0;
    normal = 0;
    cutSide = 0;

    float3 ba = b - a;
    float axisLength = length(ba);
    if (axisLength < 1e-9)
        return false;

    float3 axis = ba / axisLength;
    float slope = (rb - ra) / axisLength;

    // Points within the cone: |offset from the axis|^2 - radius^2 <= 0, a quadratic along the ray.
    float3 originFromA = rayOrigin - a;
    float alongAtOrigin = dot(originFromA, axis);
    float alongPerStep = dot(rayDirection, axis);
    float3 acrossAtOrigin = originFromA - alongAtOrigin * axis;
    float3 acrossPerStep = rayDirection - alongPerStep * axis;
    float radiusAtOrigin = ra + slope * alongAtOrigin;
    float radiusPerStep = slope * alongPerStep;

    float qa = dot(acrossPerStep, acrossPerStep) - radiusPerStep * radiusPerStep;
    float qb = dot(acrossAtOrigin, acrossPerStep) - radiusAtOrigin * radiusPerStep;
    float qc = dot(acrossAtOrigin, acrossAtOrigin) - radiusAtOrigin * radiusAtOrigin;
    // A ray along the axis stays inside or outside for its whole length.
    float coneEnter = -1e20;
    float coneExit = 1e20;
    if (qa > 1e-12)
    {
        float h = qb * qb - qa * qc;
        if (h < 0)
            return false;

        coneEnter = (-qb - sqrt(h)) / qa;
        coneExit = (-qb + sqrt(h)) / qa;
    }
    else if (qc > 0)
    {
        return false;
    }

    // Clip to the slab between the cuts.
    float slabEnter = -1e20;
    float slabExit = 1e20;
    int enterSide = 0;

    float startFacing = dot(rayDirection, startCut);
    float startHeight = dot(rayOrigin - a, startCut);
    if (abs(startFacing) > 1e-9)
    {
        float crossing = -startHeight / startFacing;
        if (startFacing > 0)
        {
            slabEnter = crossing;
            enterSide = -1;
        }
        else
        {
            slabExit = crossing;
        }
    }
    else if (startHeight < 0)
    {
        return false;
    }

    float endFacing = dot(rayDirection, endCut);
    float endHeight = dot(rayOrigin - b, endCut);
    if (abs(endFacing) > 1e-9)
    {
        float crossing = -endHeight / endFacing;
        if (endFacing < 0)
        {
            if (crossing > slabEnter)
            {
                slabEnter = crossing;
                enterSide = 1;
            }
        }
        else
        {
            slabExit = min(slabExit, crossing);
        }
    }
    else if (endHeight > 0)
    {
        return false;
    }

    float enter = max(coneEnter, slabEnter);
    if (enter > min(coneExit, slabExit))
        return false;

    distance = enter;
    if (coneEnter >= slabEnter)
    {
        // The cone's surface tilts towards its axis by the change of radius along it.
        float3 across = acrossAtOrigin + enter * acrossPerStep;
        normal = normalize(across - (radiusAtOrigin + enter * radiusPerStep) * slope * axis);
    }
    else
    {
        cutSide = enterSide;
        normal = enterSide < 0 ? -startCut : endCut;
    }

    return true;
}

psOutput psMain(psInput pin)
{
    psOutput output;

    // Ray from the eye (orthographic: along the view axis) through this pixel, intersected with the segment's round cone.
    bool isOrthographic = abs(CameraToClipSpace._34) < 1e-6;
    float3 rayDirection = isOrthographic ? float3(0, 0, -1) : normalize(pin.quadPosInCamera);
    float3 rayOrigin = isOrthographic ? pin.quadPosInCamera : 0;

    float hitDistance;
    float3 normalInCamera;
    if (FlatCaps != 0)
    {
        // Cutting each segment where it meets its neighbour, at the plane halfway between both, gives every part of
        // the tube to exactly one segment, so transparent lines don't blend their joints twice. A joint's cut face
        // lies inside the tube, where the neighbour draws the surface; only line ends show theirs.
        float3 startCut = normalize(pin.endInCamera - pin.beforeInCamera);
        float3 endCut = normalize(pin.afterInCamera - pin.startInCamera);
        int cutSide;
        if (!IntersectCutCone(rayOrigin, rayDirection, pin.startInCamera, pin.endInCamera, pin.radii.x, pin.radii.y,
                              startCut, endCut, hitDistance, normalInCamera, cutSide))
            discard;

        if ((cutSide < 0 && pin.hasNeighbours.x > 0.5) || (cutSide > 0 && pin.hasNeighbours.y > 0.5))
            discard;
    }
    else if (!IntersectRoundCone(rayOrigin, rayDirection, pin.startInCamera, pin.endInCamera, pin.radii.x, pin.radii.y,
                                 hitDistance, normalInCamera))
    {
        discard;
    }

    // The orthographic ray starts behind the segment, so only the perspective eye can sit inside it.
    if (!isOrthographic && hitDistance < 0)
        discard;

    float3 surfaceInCamera = rayOrigin + rayDirection * hitDistance;
    normalInCamera = normalize(normalInCamera);

    float3 axis = pin.endInCamera - pin.startInCamera;
    float alongSegment = saturate(dot(surfaceInCamera - pin.startInCamera, axis) / max(dot(axis, axis), 1e-12));

    // Each segment is a straight cone, so its shading would change in a step at every point. Turning the normal
    // from the segment's axis to the tangent of a curve through the neighbouring points makes it change smoothly;
    // both segments of a joint turn to the same tangent there.
    float t = alongSegment;
    float3 p0 = pin.beforeInCamera;
    float3 p1 = pin.startInCamera;
    float3 p2 = pin.endInCamera;
    float3 p3 = pin.afterInCamera;
    float3 curveTangent = 0.5 * ((p2 - p0)
                                 + 2 * (2 * p0 - 5 * p1 + 4 * p2 - p3) * t
                                 + 3 * (3 * p1 - p0 - 3 * p2 + p3) * t * t);
    float axisLengthSquared = dot(axis, axis);
    float curveTangentLengthSquared = dot(curveTangent, curveTangent);
    if (axisLengthSquared > 1e-12 && curveTangentLengthSquared > 1e-12)
    {
        float3 segmentDirection = axis * rsqrt(axisLengthSquared);
        curveTangent *= rsqrt(curveTangentLengthSquared);
        float cosTurn = dot(segmentDirection, curveTangent);

        // Rotation from one unit vector to another (Rodrigues), undefined only for a full reversal.
        if (cosTurn > -0.99)
        {
            float3 turnAxis = cross(segmentDirection, curveTangent);
            normalInCamera = normalize(normalInCamera + cross(turnAxis, normalInCamera)
                                       + cross(turnAxis, cross(turnAxis, normalInCamera)) / (1 + cosTurn));
        }
    }

    // Across the line, 0 and 1 mark the silhouette edges like on a flat strip.
    float3 side = cross(rayDirection, axis);
    float acrossLine = dot(side, side) > 1e-12 ? dot(normalInCamera, normalize(side)) * 0.5 + 0.5 : 0.5;
    float2 texCoord = float2(lerp(pin.lineCoords.x, pin.lineCoords.y, alongSegment), acrossLine);

    float4 color = lerp(pin.startColor, pin.endColor, alongSegment);

    // Fade lines that come too close to the camera.
    color.a *= saturate(-surfaceInCamera.z / FadeNearest - 1);

    float4 roughnessMetallicOcclusion = RSMOMap.Sample(WrappedSampler, texCoord);
    frag.Roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    frag.Metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    frag.Occlusion = roughnessMetallicOcclusion.z;
    frag.albedo = color;

    frag.N = normalize(mul(float4(normalInCamera, 0), CameraToWorld).xyz);
    frag.worldPosition = mul(float4(surfaceInCamera, 1), CameraToWorld).xyz;

    float4 surfaceInClipSpace = mul(float4(surfaceInCamera, 1), CameraToClipSpace);
    output.Depth = surfaceInClipSpace.z / surfaceInClipSpace.w;

    float3 surfaceInObject = mul(float4(frag.worldPosition, 1), WorldToObject).xyz;
    float3 movementInObject = lerp(pin.startMovementInObject, pin.endMovementInObject, alongSegment);
    float4 previousClipPosition = mul(float4(surfaceInObject - movementInObject, 1), PrevObjectToClipSpace);
    output.Velocity = EncodeVelocity(surfaceInClipSpace, previousClipPosition, CameraToClipSpace, PrevCameraToClipSpace);

    frag.uv = texCoord;
    frag.fog = pow(saturate(-surfaceInCamera.z / FogDistance), FogBias);

    float3 eyePosition = mul(float4(0, 0, 0, 1), CameraToWorld).xyz;
    frag.Lo = normalize(eyePosition - frag.worldPosition);

    float4 litColor = ComputePbr();
    float4 fieldColor = GetField(float4(frag.worldPosition.xyz, 0));
    litColor.rgba *= fieldColor;
    if (litColor.a < 0.04)
        discard;

    output.Color = litColor;
    output.Normal = float4(frag.N, 1);
    GetMaterialTargets(fieldColor.rgb, 1, output.BaseColor, output.Material);
    return output;
}
