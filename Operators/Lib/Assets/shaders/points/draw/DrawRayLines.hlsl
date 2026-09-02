#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
// struct Point
// {
//     float3 position;
//     float size;
// };

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
    float UseWForWidth;

    float UseWForU;
    float WidthFactor;
};

cbuffer Transforms : register(b1)
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

    float f = cornerFactors.x;

    // Point pointAA = Points[ particleId<1 ? 0: particleId-1];
    Point pointA = Points[particleId];
    Point pointB = Points[particleId + 1];

    if (IsSeparator(pointA) || IsSeparator(pointB))
    {
        output.position = 0;
        return output;
    }

    float4 forward = mul(float4(0, 0, -1, 0), CameraToWorld);

    forward = mul(float4(forward.xyz, 0), WorldToObject);
    float4 camUpInWorld = mul(float4(0, 1, 0, 0), CameraToWorld);
    float4 camUpInObject = mul(camUpInWorld, WorldToObject);

    float3 posInObject = f < 0.5 ? pointA.Position : pointB.Position;
    float3 posAInCamera = mul(float4(pointA.Position, 1), ObjectToCamera).xyz;
    float3 posBInCamera = mul(float4(pointB.Position, 1), ObjectToCamera).xyz;
    float4 lineInCamera = float4(posAInCamera - posBInCamera, 1);
    float3 forwardInCamera = float3(0, 0, -1);

    float3 lineCenterInCamera = lerp(posAInCamera, posBInCamera, 0.5);
    float3 sideInCamera = normalize(cross(lineCenterInCamera, lineInCamera.xyz));

    output.texCoord = float2(
        f,
        cornerFactors.y / 2 + 0.5);

    float4 posInCamera = mul(float4(posInObject, 1), ObjectToCamera);

    float pFx1 = lerp(pointA.FX1, pointB.FX1, f);
    float pFx2 = lerp(pointA.FX2, pointB.FX2, f);

    float sizeFxFactor = WidthFactor < 0.5 ? 1 : ((WidthFactor < 1.5) ? pFx1 : pFx2);

    posInCamera.xyz += sideInCamera * Size * sizeFxFactor / 1000 * cornerFactors.y;

    output.position = mul(posInCamera, CameraToClipSpace);
    output.fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
    output.color = Color * (f < 0.5 ? pointA.Color : pointB.Color);
    return output;
}

float4 psMain(psInput input) : SV_TARGET
{
    float4 imgColor = texture2.Sample(texSampler, input.texCoord);
    float dFromLineCenter = abs(input.texCoord.y - 0.5) * 2;

    float4 col = input.color * imgColor;
    col.rgb = lerp(col.rgb, FogColor.rgb, input.fog);
    return clamp(col, float4(0, 0, 0, 0), float4(1000, 1000, 1000, 1));
}
