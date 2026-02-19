#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

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
    float4x4 transformSampleSpace;

    float3 Center;
    float Amount;
    float3 AmountDistribution;
    float RotationLookupDistance;
    float OffsetSpace;
    float UseVertexSelection;
}

StructuredBuffer<PbrVertex> SourceVertices : t0;
Texture2D<float4> inputTexture : register(t1);

RWStructuredBuffer<PbrVertex> ResultVertices : u0;
sampler texSampler : register(s0);

static float3 variationOffset;
static float3x3 TBN;

float3 GetOffset(float3 pos, float3 variation)
{
    pos -= Center;
    float3 posInObject = mul(float4(pos.xyz, 0), transformSampleSpace).xyz;
    float4 c = inputTexture.SampleLevel(texSampler, posInObject.xy * float2(1, -1) + float2(0.5, 0.5), 0.0);
    float3 offset = (c.rgb - 0.5) * Amount * AmountDistribution;

    if (OffsetSpace > 0.5)
        offset = mul(float4(offset, 0), ObjectToWorld).xyz;

    return offset;
}

[numthreads(64, 1, 1)] void main(uint3 i
                                 : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourceVertices.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
    {
        return;
    }

    PbrVertex v = SourceVertices[i.x];
    ResultVertices[i.x] = SourceVertices[i.x];

    float weight = 1;
    float3 offset = 0;

    float3 posInWorld = v.Position;

    float3 pos = v.Position;
    pos -= Center;
    float3 posInObject = mul(float4(pos.xyz, 0), transformSampleSpace).xyz;
    float4 c = inputTexture.SampleLevel(texSampler, posInObject.xy * float2(1, -1) + float2(0.5, 0.5), 0.0);
    offset = GetOffset(posInWorld, variationOffset) * weight;

    float selection = UseVertexSelection > 0.5 ? v.Selected : 1;
    offset *= selection;

    float3 newPos = posInWorld + offset;
    float lookUpDistance = RotationLookupDistance;

    float3 tAnchor = posInWorld + v.Tangent * lookUpDistance;
    float3 tAnchor2 = posInWorld - v.Tangent * lookUpDistance;

    weight *= selection;
    float3 newTangent = normalize(tAnchor + GetOffset(tAnchor, variationOffset) * weight - newPos);
    float3 newTangent2 = -normalize(tAnchor2 + GetOffset(tAnchor2, variationOffset) * weight - newPos);
    ResultVertices[i.x].Tangent = lerp(newTangent, newTangent2, 0.5);

    float3 bAnchor = posInWorld + v.Bitangent * lookUpDistance;
    float3 bAnchor2 = posInWorld - v.Bitangent * lookUpDistance;

    float3 newBitangent = normalize(bAnchor + GetOffset(bAnchor, variationOffset) * weight - newPos);
    float3 newBitangent2 = -normalize(bAnchor2 + GetOffset(bAnchor2, variationOffset) * weight - newPos);

    ResultVertices[i.x].Bitangent = lerp(newBitangent, newBitangent2, 0.4);
    ResultVertices[i.x].Normal = cross(ResultVertices[i.x].Tangent, ResultVertices[i.x].Bitangent);
    ResultVertices[i.x].Position = v.Position + offset;
}
