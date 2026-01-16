#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"

static const float2 kCorners[6] = {
    float2(-1.0, -1.0), // tri 0
    float2(1.0, -1.0),
    float2(-1.0, 1.0),
    float2(-1.0, 1.0), // tri 1
    float2(1.0, -1.0),
    float2(1.0, 1.0)};

static const float2 kUV[6] = {
    float2(0.0, 1.0),
    float2(1.0, 1.0),
    float2(0.0, 0.0),
    float2(0.0, 0.0),
    float2(1.0, 1.0),
    float2(1.0, 0.0)};

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

cbuffer PrevTransforms : register(b1)
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

cbuffer FloatParams : register(b2)
{
    float4 Color;

    float PointSize;
    float CutOffTransparent;
    float FadeNearest;
    float VelocityMaxJump;

    float VelocityStretch;
};

cbuffer IntParams : register(b3)
{
    int SegmentCount;
    int ScaleFX;
    int UsePointScale;
};

cbuffer FogParams : register(b4)
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

// For shader graph params
cbuffer Params : register(b5)
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
StructuredBuffer<Point> PointsPrev : register(t1);
Texture2D<float4> texture2 : register(t2);

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Additional Resources ==========================================
/*{RESOURCES(t3)}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------
float4 GetField(float4 p)
{
    // p.xyz = mul(float4(p.xyz, 1), WorldToObject).xyz;
    float4 f = 1;
    /*{FIELD_CALL}*/
    return f;
}

float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}
//===================================================================

// Safe normalize (keeps fallback if tiny)
float2 safe_norm2(float2 v, float2 fallback)
{
    float l2 = dot(v, v);
    return l2 > 1e-12 ? v * rsqrt(l2) : fallback;
}

psInput vsMain(uint id : SV_VertexID)
{
    psInput o;

    float VelBlendStart = 0.000;
    float VelBlendEnd = 0.001;

    float VelSpeed1Unit = 0.1;

    const uint q = id % 6;
    const uint i = id / 6;

    Point P = Points[i];
    Point PP = PointsPrev[i];

    float2 corner = kCorners[q];
    o.texCoord = kUV[q];

    float sizeFx = (ScaleFX == 0.0) ? 1.0 : ((ScaleFX == 1.0) ? P.Scale.x : P.Scale.y);
    float2 baseSize = PointSize * sizeFx * (UsePointScale != 0 ? P.Scale.xy : 1.0.xx) * 0.2;

    float4 obj = float4(P.Position, 1.0);
    float4 objP = float4(PP.Position, 1.0);

    float4 w = mul(obj, ObjectToWorld);
    float4 wP = mul(objP, PrevObjectToWorld);
    float4 cam = mul(w, WorldToCamera);
    float4 camP = mul(wP, PrevWorldToCamera);

    o.color = Color * P.Color;
    float nearFade = saturate(-cam.z / FadeNearest - 1.0);
    o.color.a *= nearFade;

    // Jump guard in object space
    float objDist = distance(P.Position, PP.Position);
    float useVel = (objDist <= VelocityMaxJump) ? 1.0 : 0.0;

    // Aspect from projection (resolution-independent units)
    float Aspect = CameraToClipSpace._22 / CameraToClipSpace._11;

    float4 clip = mul(cam, CameraToClipSpace);
    float4 clipP = mul(camP, PrevCameraToClipSpace);
    float2 ndc = clip.xy / max(1e-6, clip.w);
    float2 ndcP = clipP.xy / max(1e-6, clipP.w);

    float2 vU = (ndc - ndcP); // * useVel;
    vU.x *= Aspect;

    float speedU = length(vU);

    const float2 ex = float2(1, 0), ey = float2(0, 1);
    float2 dirVel = (speedU > 1e-6) ? normalize(vU) : ey;

    float2 perpVel = float2(dirVel.y, -dirVel.x);

    // Blend to default
    float wAlign = clamp((speedU - VelBlendStart) / max(1e-6, (VelBlendEnd - VelBlendStart)), 0, 1);
    float2 dir = normalize(lerp(ey, dirVel, wAlign));
    float2 per = normalize(lerp(ex, perpVel, wAlign));

    float f = saturate(speedU / max(1e-6, VelSpeed1Unit));
    float stretchTarget = lerp(1.0, VelocityStretch, f);
    float stretch = clamp(lerp(1.0, stretchTarget, wAlign), 0, 10);

    float thickness = 0.5 * baseSize.x;
    float lengthHx = 0.5 * baseSize.y * stretch;
    float2 offset = corner.x * thickness * per + corner.y * lengthHx * dir;
    float4 camQuad = cam;
    camQuad.xy += offset;
    o.position = mul(camQuad, CameraToClipSpace);

    // Field & fog
    o.color *= GetField(w);
    o.fog = pow(saturate(-cam.z / FogDistance), FogBias);
    return o;
}

float4 psMain(psInput i) : SV_TARGET
{
    float4 tex = texture2.Sample(ClampedSampler, i.texCoord);
    float4 col = i.color * tex;

    if (CutOffTransparent > 0.0 && col.a < CutOffTransparent)
        discard;

    col.rgb = lerp(col.rgb, 0.0.xxx, i.fog);
    return col;
}
