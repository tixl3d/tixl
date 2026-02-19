#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"

// cbuffer Params : register(b0)
// {
//     //float4 Color;    
//     //float TestParamA;
//     //float AlphaCutOff;
//     //float UseCubeMap;
// };


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

cbuffer CamTransforms : register(b1)
{
    float4x4 RefCameraToClipSpace;
    float4x4 ClipSpaceToRefCamera;
    float4x4 WorldToRefCamera;
    float4x4 RefCameraToWorld;
    float4x4 WorldToRefClipSpace;
    float4x4 RefClipSpaceToWorld;
    float4x4 __ObjectToWorld;
    float4x4 __WorldToObject;
    float4x4 ObjectToRefCamera;
    float4x4 ObjectToRefClipSpace;
};

struct psInput
{
    //float2 texCoord : TEXCOORD;
    float4 pixelPosition : SV_POSITION;
    float4 vertexPosInObject : VERTEXPOS;
};

sampler texSampler : register(s0);

StructuredBuffer<PbrVertex> PbrVertices : t0;
StructuredBuffer<int3> FaceIndices : t1;
Texture2D<float4> BaseColorMap2 : register(t2);
TextureCube<float4> CubeMap : register(t3);

psInput vsMain(uint id: SV_VertexID)
{
    psInput output;

    int faceIndex = id / 3;
    int faceVertexIndex = id % 3;

    PbrVertex vertex = PbrVertices[FaceIndices[faceIndex][faceVertexIndex]];

    output.vertexPosInObject = float4( vertex.Position,1);

    float4 posInObject = float4(vertex.TexCoord * 2 - 1, 0, 1);
    float4 posInClipSpace = mul(posInObject, ObjectToClipSpace);
    output.pixelPosition =  posInClipSpace;
    return output;
}


float4 psMain(psInput pin) : SV_TARGET
{
    float4 vertexInClipSpace = mul(pin.vertexPosInObject, ObjectToRefClipSpace);
    vertexInClipSpace.xyz /= vertexInClipSpace.w;    

    float2 uv = vertexInClipSpace.xy * float2(0.5, -0.5) + 0.5;
    float4 albedo = BaseColorMap2.Sample(texSampler, uv);
    return albedo;
}
