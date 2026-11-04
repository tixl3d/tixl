#include "shared/point.hlsl"

// Keeps the constant buffer layout of DrawLines, because the operator graph that feeds it is shared.
cbuffer Params : register(b0)
{
    float4 Color;

    float Size;
    float ShrinkWithDistance;
    float OffsetU;
    float UvScale;

    float FadeTooLong;
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
    float fog : FOG;
};

sampler texSampler : register(s0);

StructuredBuffer<Point> Points : register(t0);
Texture2D<float4> texture2 : register(t1);

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;

    // Every three points form one triangle; a separator collapses its triangle so it isn't drawn.
    uint triangleStart = id - id % 3;
    bool isSeparated = IsSeparator(Points[triangleStart])
                       || IsSeparator(Points[triangleStart + 1])
                       || IsSeparator(Points[triangleStart + 2]);

    Point p = Points[id];
    output.position = isSeparated
                          ? float4(0, 0, 0, 1)
                          : mul(float4(p.Position, 1), ObjectToClipSpace);

    float4 posInCamSpace = mul(float4(p.Position, 1), ObjectToCamera);
    output.fog = pow(saturate(-posInCamSpace.z / FogDistance), FogBias);
    output.color = Color * p.Color;
    return output;
}

float4 psMain(psInput input) : SV_TARGET
{
    // The texture is a solid fallback here; sampling it keeps the binding layout of DrawLines.
    float4 col = input.color * texture2.SampleLevel(texSampler, float2(0.5, 0.5), 0);
    col.rgb = lerp(col.rgb, FogColor.rgb, input.fog);
    return clamp(col, float4(0, 0, 0, 0), float4(1000, 1000, 1000, 1));
}
