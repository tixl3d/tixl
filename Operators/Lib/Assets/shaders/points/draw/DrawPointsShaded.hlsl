#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"
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
    float3 centerInCamera : POSITION2;
    float3 quadPosInCamera : POSITION3;
    float3 movementInObject : MOTION;
    float radius : RADIUS;
};

struct psOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Velocity : SV_Target2;

    // The sphere's front lies between the quad and the camera, so depth only ever moves closer.
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

/** A PointSize of 1 makes a quad, and so a sphere, 0.1 world units across. */
static const float QuadHalfSizePerPointSize = 0.05;

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;

    int quadIndex = id % 6;
    int particleId = id / 6;
    Point pointDef = Points[particleId];

    float3 quadPos = Corners[quadIndex];
    output.texCoord = (quadPos.xy * 0.5 + 0.5);

    float4 posInObject = float4(pointDef.Position, 1);
    float3 centerInCamera = mul(posInObject, ObjectToCamera).xyz;
    output.centerInCamera = centerInCamera;
    output.color = pointDef.Color * Color;

    // An unbound or shorter buffer reads zeros; a zero rotation marks such a point.
    uint previousCount, previousStride;
    PreviousPoints.GetDimensions(previousCount, previousStride);
    output.movementInObject = 0;
    if ((uint)particleId < previousCount)
    {
        Point previousPoint = PreviousPoints[particleId];
        if (dot(previousPoint.Rotation, previousPoint.Rotation) > 1e-6)
            output.movementInObject = pointDef.Position - previousPoint.Position;
    }

    // Shrink too close particles
    float tooCloseFactor = saturate(-centerInCamera.z / FadeNearest - 1);
    output.color.a *= tooCloseFactor;

    float sizeFactor = ScaleFactorMode == 0
                           ? 1
                       : (ScaleFactorMode == 1) ? pointDef.FX1
                                                : pointDef.FX2;

    float radius = PointSize * sizeFactor * (UsePointScale != 0 ? pointDef.Scale.x : 1) * QuadHalfSizePerPointSize;
    output.radius = radius;

    // The quad faces the eye and covers the cone of rays touching the sphere. Facing the camera plane instead would
    // misplace spheres away from the view center, where perspective shows them from the side.
    bool isOrthographic = abs(CameraToClipSpace._34) < 1e-6;
    float distanceToEye = length(centerInCamera);
    float3 towardsEye = isOrthographic || distanceToEye < 1e-6 ? float3(0, 0, 1) : -centerInCamera / distanceToEye;
    float3 right = cross(float3(0, 1, 0), towardsEye);
    right = dot(right, right) > 1e-8 ? normalize(right) : float3(1, 0, 0);
    float3 up = cross(towardsEye, right);
    float halfSize = isOrthographic
                         ? radius
                         : radius * distanceToEye / sqrt(max(distanceToEye * distanceToEye - radius * radius, radius * radius * 0.01));

    float3 quadPosInCamera = centerInCamera + (right * quadPos.x + up * quadPos.y) * halfSize;
    output.quadPosInCamera = quadPosInCamera;
    output.position = mul(float4(quadPosInCamera, 1), CameraToClipSpace);

    // Fog
    output.fog = pow(saturate(-centerInCamera.z / FogDistance), FogBias);
    return output;
}

static float3 LightPosition = float3(1, 2, 0);

psOutput psMain(psInput pin)
{
    psOutput output;

    // Ray from the eye (orthographic: along the view axis) through this pixel of the quad, intersected with the sphere.
    bool isOrthographic = abs(CameraToClipSpace._34) < 1e-6;
    float3 rayDirection = isOrthographic ? float3(0, 0, -1) : normalize(pin.quadPosInCamera);
    float3 rayOrigin = isOrthographic ? pin.quadPosInCamera : 0;
    float3 centerToOrigin = rayOrigin - pin.centerInCamera;
    float b = dot(centerToOrigin, rayDirection);
    float h = b * b - (dot(centerToOrigin, centerToOrigin) - pin.radius * pin.radius);
    if (h < 0)
        discard;

    float3 surfaceInCamera = rayOrigin + rayDirection * (-b - sqrt(h));
    float3 normalInCamera = (surfaceInCamera - pin.centerInCamera) / pin.radius;

    // Sample input textures to get shading model params.
    float4 albedo = pin.color;
    
    float4 roughnessMetallicOcclusion = RSMOMap.Sample(WrappedSampler, pin.texCoord);
    frag.Roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    frag.Metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    frag.Occlusion = roughnessMetallicOcclusion.z;
    frag.albedo = albedo;

    frag.N = normalize(mul(float4(normalInCamera, 0), CameraToWorld).xyz);
    frag.worldPosition = mul(float4(surfaceInCamera, 1), CameraToWorld).xyz;

    float4 surfaceInClipSpace = mul(float4(surfaceInCamera, 1), CameraToClipSpace);
    output.Depth = surfaceInClipSpace.z / surfaceInClipSpace.w;

    // Spheres look the same from every angle, so the surface point moved with the point's center.
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

    output.Normal = float4 (frag.N,1);

    return output;
}
