#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float3 OffsetByTBN;
    float OffsetScale;
}

StructuredBuffer<PbrVertex> Vertices : t0;   // input
RWStructuredBuffer<LegacyPoint> ResultPoints : u0; // output

[numthreads(256, 4, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x;
    PbrVertex v = Vertices[index];

    ResultPoints[index].Position = v.Position + OffsetByTBN.x * v.Tangent * OffsetScale + OffsetByTBN.y * v.Bitangent * OffsetScale + OffsetByTBN.z * v.Normal * OffsetScale;

    ResultPoints[index].W = v.Selected;

    float3x3 m = float3x3(v.Tangent, v.Bitangent, v.Normal);
    float4 rot = normalize(qFromMatrix3Precise(transpose(m)));

    ResultPoints[index].Rotation = normalize(rot);
    ResultPoints[index].Color = 1;
    ResultPoints[index].Selected = v.Selected;
    ResultPoints[index].Stretch = 1;
}