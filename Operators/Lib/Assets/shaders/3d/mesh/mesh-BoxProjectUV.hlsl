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
    float4x4 Transform;
    float TexCoord2;
}

StructuredBuffer<PbrVertex> Vertices : register(t0);
RWStructuredBuffer<PbrVertex> ResultVertices : register(u0);

[numthreads(256,4,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint vertexIndex = i.x;
    uint stride, vertexCount;
    Vertices.GetDimensions(vertexCount, stride);
    if(vertexIndex >= vertexCount) {
        return;
    }

    PbrVertex v = Vertices[vertexIndex];

    // The graph offsets x and y by -0.5 so a unit box maps to 0..1; apply the same centering to all axes.
    float3 posInBox = mul(float4(v.Position, 1), Transform).xyz + float3(0.5, 0.5, 0);

    // Normals transform with the inverse transpose, so face selection follows rotation and non-uniform stretch.
    float3x3 m = (float3x3)Transform;
    float3x3 normalMatrix = float3x3(cross(m[1], m[2]), cross(m[2], m[0]), cross(m[0], m[1]));
    float handedness = sign(dot(m[0], cross(m[1], m[2])));
    float3 n = mul(v.Normal, normalMatrix) * handedness;
    float3 a = abs(n);

    // Each face is mapped as seen from outside the box, so textures are never mirrored.
    float2 uv;
    if (a.x > a.y && a.x > a.z)
    {
        uv = float2(-sign(n.x) * posInBox.z, posInBox.y);
    }
    else if (a.y > a.z)
    {
        uv = float2(posInBox.x, -sign(n.y) * posInBox.z);
    }
    else
    {
        uv = float2((n.z < 0 ? -1 : 1) * posInBox.x, posInBox.y);
    }
    uv += 0.5;

    if ((bool)TexCoord2)
    {
        v.TexCoord2 = uv;
    }
    else
    {
        v.TexCoord = uv;
    }

    ResultVertices[vertexIndex] = v;
}
