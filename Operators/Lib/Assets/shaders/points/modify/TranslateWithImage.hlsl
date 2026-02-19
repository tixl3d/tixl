#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"
#include "shared/hash-functions.hlsl"

cbuffer Params : register(b0)
{
    float4x4 transformSampleSpace;
    float4x4 TransformMatrix;

    float Strength;
    float3 Translate;

    float3 Scale;
    float ScaleUniform;

    float3 Rotate;
    float Scatter;

    float ScaleFx1;
    float ScaleFx2;
    float2 GainAndBias;

    float3 Center;
    float StrengthOffset;
}

cbuffer Params : register(b1)
{
    int StrengthFactor;
    int Channel;
    int TranslationSpace;
}

StructuredBuffer<Point> Points : register(t0);
Texture2D<float4> inputTexture : register(t1);
RWStructuredBuffer<Point> ResultPoints : register(u0); // output

sampler texSampler : register(s0);

float3 ExtractScale(float4x4 TransformMatrix)
{
    float3 scale;
    scale.x = length(TransformMatrix._m00_m01_m02);
    scale.y = length(TransformMatrix._m10_m11_m12);
    scale.z = length(TransformMatrix._m20_m21_m22);
    return scale;
}

[numthreads(256, 4, 1)] void main(uint3 i
                                  : SV_DispatchThreadID)
{
    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount, stride);
    if (i.x >= pointCount)
        return;

    uint index = i.x;

    Point p = Points[index];

    float3 pos = p.Position;
    pos -= Center;

    float3 posInObject = mul(float4(pos.xyz, 0), transformSampleSpace).xyz;
    float4 c = inputTexture.SampleLevel(texSampler, posInObject.xy * float2(0.5, -0.5) + float2(0.5, 0.5), 0.0);
    float gray = (c.r + c.g + c.b) / 3;

    float f = gray + (hash11u(index) - 0.5) * Scatter;

    f = ApplyGainAndBias(f, GainAndBias);

    float strength = Strength * (f + StrengthOffset) * (StrengthFactor == 0 ? 1 : (StrengthFactor == 1) ? p.FX1
                                                                                                        : p.FX2);

    float4 orgRot = p.Rotation;
    float4 rotation = orgRot;

    if (TranslationSpace < 0.5)
    {
        pos.xyz = 0;
        rotation = float4(0, 0, 0, 1);
    }

    float3 pLocal = pos;
    pos = mul(float4(pos, 1), TransformMatrix).xyz;

    float4 newRotation = rotation;

    float3 scale = ExtractScale(TransformMatrix);

    // Remove scale from the matrix to get pure rotation
    float3x3 rotationMatrix = float3x3(
        TransformMatrix._m00_m01_m02 / scale.x,
        TransformMatrix._m10_m11_m12 / scale.y,
        TransformMatrix._m20_m21_m22 / scale.z);

    newRotation = normalize(qFromMatrix3Precise(transpose(rotationMatrix)));

    // Adjust rotation in point space
    if (TranslationSpace < 0.5)
    {
        newRotation = qMul(orgRot, newRotation);
    }
    else
    {
        newRotation = qMul(newRotation, orgRot);
    }

    if (TranslationSpace == 0)
    {
        pos.xyz = qRotateVec3(pos.xyz, orgRot).xyz;
        pos += p.Position;

        // Apply scale to Stretch
        p.Scale *= lerp(1, scale, strength);
    }

    p.Position = lerp(p.Position, pos.xyz, strength);
    p.Rotation = qSlerp(p.Rotation, newRotation, strength);

    ResultPoints[index] = p;
}