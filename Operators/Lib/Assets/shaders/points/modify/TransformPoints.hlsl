#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformMatrix;
    float Strength;
    // float UpdateRotation;
    // float ScaleW;
    // float OffsetW;
    // float WIsWeight;
}

cbuffer Params : register(b1)
{
    int CoordinateSpace;
    int StrengthFactor;
}

StructuredBuffer<Point> SourcePoints : t0;
RWStructuredBuffer<Point> ResultPoints : u0;

static const float PointSpace = 0;
static const float ObjectSpace = 1;
static const float WorldSpace = 2;

float3 ExtractScale(float4x4 TransformMatrix)
{
    float3 scale;
    scale.x = length(TransformMatrix._m00_m01_m02);
    scale.y = length(TransformMatrix._m10_m11_m12);
    scale.z = length(TransformMatrix._m20_m21_m22);
    return scale;
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
        return;

    Point p = SourcePoints[i.x];

    float3 pos = p.Position;
    float4 orgRot = p.Rotation;
    float4 rotation = orgRot;

    if (CoordinateSpace < 0.5)
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
    if (CoordinateSpace < 0.5)
    {
        newRotation = qMul(orgRot, newRotation);
    }
    else
    {
        newRotation = qMul(newRotation, orgRot);
    }

    float strength = Strength * (StrengthFactor == 0
                                     ? 1
                                 : (StrengthFactor == 1) ? p.FX1
                                                         : p.FX2);

    if (CoordinateSpace == 0)
    {
        pos.xyz = qRotateVec3(pos.xyz, orgRot).xyz;
        pos += p.Position;

        // Apply scale to Stretch
        p.Scale *= lerp(1, scale, strength);
    }

    p.Position = lerp(p.Position, pos.xyz, strength);
    p.Rotation = qSlerp(p.Rotation, newRotation, strength);
    ResultPoints[i.x] = p;
}