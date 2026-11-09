#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

/** Caps the corner offset (in half line widths) so sharp turns get blunted instead of spiking out. */
static const float MiterLimit = 2;

static const float3 Corners[] =
    {
        float3(0, -1, 0),
        float3(1, -1, 0),
        float3(1, 1, 0),
        float3(1, 1, 0),
        float3(0, 1, 0),
        float3(0, -1, 0),
};

cbuffer Params : register(b0)
{
    float4 Color;

    float Size;
    float ShrinkWithDistance;
    float OffsetU;
    float UvScale;

    float FadeTooLong;
    float EnableZWrite;
    float BlendMode;
};

cbuffer IntParams : register(b1)
{
    int UvMode;
    int WidthFX;
    int UseWForU;
    int UseWForWidth;
};

cbuffer Transforms : register(b2)
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

cbuffer FogParams : register(b3)
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
    float3 toCameraInCamSpace : TEXCOORD1;
};

sampler texSampler : register(s0);

float3 SafeNormalize(float3 v, float3 fallback)
{
    float len = length(v);
    return len > 1e-6 ? v / len : fallback;
}

StructuredBuffer<Point> Points : register(t0);
Texture2D<float4> texture2 : register(t1);

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;
    float discardFactor = 1;

    uint SegmentCount, Stride;
    Points.GetDimensions(SegmentCount, Stride);

    float4 aspect = float4(CameraToClipSpace[1][1] / CameraToClipSpace[0][0], 1, 1, 1);
    int quadIndex = id % 6;
    uint particleId = id / 6;
    float3 cornerFactors = Corners[quadIndex];

    Point pointAA = Points[particleId < 1 ? 0 : particleId - 1];
    Point pointA = Points[particleId];
    Point pointB = Points[particleId + 1];
    Point pointBB = Points[particleId + 2 > SegmentCount - 1
                               ? SegmentCount - 1
                               : particleId + 2];

    float3 pointAPos = pointA.Position;
    float3 pointBPos = pointB.Position;

    float len = length(pointAPos - pointBPos);
    float fade = smoothstep(2 * FadeTooLong, FadeTooLong, len);
    if (fade < 0.001)
        discardFactor = 0;

    float f = cornerFactors.x;
    float3 posInObject = f < 0.5
                             ? pointAPos
                             : pointBPos;

    float4 aaInScreen = mul(float4(pointAA.Position, 1), ObjectToClipSpace) * aspect;
    aaInScreen /= aaInScreen.w;
    float4 aInScreen = mul(float4(pointA.Position, 1), ObjectToClipSpace) * aspect;
    if (aInScreen.z < -0)
        discardFactor = 0;
    aInScreen /= aInScreen.w;

    float4 bInScreen = mul(float4(pointB.Position, 1), ObjectToClipSpace) * aspect;
    if (bInScreen.z < -0)
        discardFactor = 0;

    bInScreen /= bInScreen.w;
    float4 bbInScreen = mul(float4(pointBB.Position, 1), ObjectToClipSpace) * aspect;
    bbInScreen /= bbInScreen.w;

    float3 direction = (aInScreen - bInScreen).xyz;
    float3 directionA = particleId > 0
                            ? (aaInScreen - aInScreen).xyz
                            : direction;
    float3 directionB = particleId < SegmentCount - 2
                            ? (bInScreen - bbInScreen).xyz
                            : direction;

    // Duplicate points give zero-length directions; fall back instead of producing NaN vertices.
    float3 normal = SafeNormalize(cross(direction, float3(0, 0, 1)), float3(0, 1, 0));
    float3 normalA = SafeNormalize(cross(directionA, float3(0, 0, 1)), normal);
    float3 normalB = SafeNormalize(cross(directionB, float3(0, 0, 1)), normal);

    if (IsSeparator(pointAA) || isinf(pointAA.Scale.x))
    {
        normalA = normal;
    }
    if (IsSeparator(pointBB) || isinf(pointBB.Scale.x))
    {
        normalB = normal;
    }

    float3 neighbourNormal = lerp(normalA, normalB, f);
    float3 miterNormal = SafeNormalize(normal + neighbourNormal, normal);
    float4 pos = lerp(aInScreen, bInScreen, f);

    float4 posInCamSpace = mul(float4(posInObject, 1), ObjectToCamera);
    posInCamSpace.xyz /= posInCamSpace.w;
    posInCamSpace.w = 1;

    float pFx1 = lerp(pointA.FX1, pointB.FX1, f);
    float pFx2 = lerp(pointA.FX2, pointB.FX2, f);

    float texFxFactor = WidthFX == 0 ? 1 : ((WidthFX == 1) ? pFx1 : pFx2);

    float u = f;
    switch (UvMode)
    {
    case 0:
        u = (particleId + f) / SegmentCount;
        break;
    case 1:
        u = pFx1;
        break;
    case 2:
        u = pFx2;
        break;
    }

    output.texCoord = float2(u * UvScale + OffsetU, cornerFactors.y / 2 + 0.5);

    float widthAtPoint = lerp(pointA.Scale.x, pointB.Scale.x, f);
    float widthFxFactor = WidthFX == 0 ? 1 : ((WidthFX == 1) ? pFx1 : pFx2);
    float thickness = Size * discardFactor * lerp(1, 1 / (posInCamSpace.z), ShrinkWithDistance) * widthFxFactor;
    thickness *= widthAtPoint; // UseWForWidth < 0 ? lerp(1, 1-widthAtPoint, -UseWForWidth)
                               //: lerp(1, widthAtPoint, UseWForWidth) ;

    float miterScale = min(1 / max(dot(miterNormal, normal), 0.001), MiterLimit);
    pos -= cornerFactors.y * 0.1 * thickness * miterScale * float4(miterNormal, 0);

    output.position = pos / aspect;

    float3 n = f < 0.5
                   ? cross(pointA.Position - pointAA.Position, pointA.Position - pointB.Position)
                   : cross(pointB.Position - pointA.Position, pointB.Position - pointBB.Position);
    n = normalize(n);

    output.fog = pow(saturate(-posInCamSpace.z / FogDistance), FogBias);
    output.toCameraInCamSpace = -posInCamSpace.xyz;

    output.color = Color * lerp(pointA.Color, pointB.Color, cornerFactors.x);

    output.color.a *= fade;

    return output;
}

struct psOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
};

/** Lines face the camera position like a billboard. Lines that don't write depth or that blend additively must leave
    the normal buffer untouched, so they write zero with zero alpha, which every blend mode except Multiply and Invert keeps. */
float4 GetLineNormal(float3 toCameraInCamSpace)
{
    bool replacesSurface = EnableZWrite > 0.5 && (BlendMode < 0.5 || abs(BlendMode - 4.0) < 0.5);
    if (!replacesSurface)
        return 0;

    return float4(normalize(mul(float4(toCameraInCamSpace, 0), CameraToWorld).xyz), 1);
}

psOutput psMain(psInput input)
{
    float4 imgColor = texture2.Sample(texSampler, input.texCoord);
    float dFromLineCenter = abs(input.texCoord.y - 0.5) * 2;

    float4 col = input.color * imgColor;
    col.rgb = lerp(col.rgb, FogColor.rgb, input.fog);
    psOutput output;
    output.Color = clamp(col, float4(0, 0, 0, 0), float4(1000, 1000, 1000, 1));
    output.Normal = GetLineNormal(input.toCameraInCamSpace);
    return output;
}
