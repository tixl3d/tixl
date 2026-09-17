// Paints an output canvas from the room's geometry: every surface quad is rasterized where it lands on the
// canvas (its TexCoord2), and each pixel looks from the viewer through its world position into an
// environment — an equirectangular image or a cube map — so one scene renders once and lands continuous
// across every wall. Consumes the mesh [StageGeometry] → [GeometryToMesh] produce.
#include "shared/pbr.hlsl"

static const float PI = 3.14159265;

cbuffer Params : register(b0)
{
    float3 ViewerPosition;
    float UseCubeMap;
    float4 Color;
    float RotateY;
    float FlipHorizontal;
    float2 __padding;
};

struct psInput
{
    float4 pixelPosition : SV_POSITION;
    float3 worldPosition : TEXCOORD0;
};

sampler texSampler : register(s0);

StructuredBuffer<PbrVertex> PbrVertices : register(t0);
StructuredBuffer<int3> FaceIndices : register(t1);
Texture2D<float4> Image : register(t2);
TextureCube<float4> CubeMap : register(t3);

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;
    int faceIndex = id / 3;
    int faceVertexIndex = id % 3;
    PbrVertex vertex = PbrVertices[FaceIndices[faceIndex][faceVertexIndex]];

    // The canvas coordinate is the clip position: 0..1 with Y down becomes -1..1 with Y up.
    float2 canvas = vertex.TexCoord2;
    output.pixelPosition = float4(canvas.x * 2 - 1, 1 - canvas.y * 2, 0.5, 1);
    output.worldPosition = vertex.Position;
    return output;
}

float4 psMain(psInput pin) : SV_TARGET
{
    float3 dir = normalize(pin.worldPosition - ViewerPosition);

    // Turn the environment around the viewer, so the scene's front can face any wall.
    float angle = RotateY * PI / 180;
    float s = sin(angle);
    float c = cos(angle);
    dir = float3(dir.x * c - dir.z * s, dir.y, dir.x * s + dir.z * c);
    if (FlipHorizontal > 0.5)
        dir.x = -dir.x;

    float4 color;
    if (UseCubeMap > 0.5)
    {
        color = CubeMap.Sample(texSampler, dir);
    }
    else
    {
        float2 uv = float2(0.5 + atan2(dir.x, dir.z) / (2 * PI), acos(clamp(dir.y, -1, 1)) / PI);
        color = Image.Sample(texSampler, uv);
    }

    return color * Color;
}
