#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
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
    float4 Color;    
    float AlphaCutOff;
    float BlurLevel;
    float UseCubeMap;
};


struct psInput
{
    float2 texCoord : TEXCOORD;
    float4 pixelPosition : SV_POSITION;
    float3 normal : POSITION;
};

sampler texSampler : register(s0);

StructuredBuffer<PbrVertex> PbrVertices : t0;
StructuredBuffer<int3> FaceIndices : t1;

Texture2D<float4> BaseColorMap2 : register(t2);
TextureCube<float4> CubeMap : register(t3);

psInput vsMain(uint id: SV_VertexID)
{
    psInput output;

    int faceIndex = id / 3;//  (id % verticesPerInstance) / 3;
    int faceVertexIndex = id % 3;

    PbrVertex vertex = PbrVertices[FaceIndices[faceIndex][faceVertexIndex]];

    float4 posInObject = float4( vertex.Position,1);

    float4 posInClipSpace = mul(posInObject, ObjectToClipSpace);
    output.pixelPosition = posInClipSpace;

    output.normal = vertex.Normal;

    float2 uv = vertex.TexCoord;
    output.texCoord = float2(uv.x , 1- uv.y);
    return output;
}


float4 psMain(psInput pin) : SV_TARGET
{
    if(UseCubeMap > 0.5) {
        
        uint width, height, levels;
        CubeMap.GetDimensions(0, width, height, levels);

        float level = BlurLevel * levels;
        int baseLevel = (int)level;

        float4 c1 = CubeMap.SampleLevel(texSampler, pin.normal.xyz, baseLevel);
        float4 c2 = CubeMap.SampleLevel(texSampler, pin.normal.xyz, baseLevel + 1);
        float4 albedo = lerp(c1, c2, level - baseLevel);
        return albedo * Color;        
    }
    else 
    {
        uint width, height, levels;
        BaseColorMap2.GetDimensions(0, width, height, levels);
        float level = BlurLevel * levels;
        int baseLevel = (int)level;

        float4 albedo = 0;
        if(BlurLevel > 0) 
        {
            float4 c1 = BaseColorMap2.SampleLevel(texSampler, pin.texCoord, baseLevel);
            float4 c2 = BaseColorMap2.SampleLevel(texSampler, pin.texCoord, baseLevel + 1);
            albedo = lerp(c1, c2, level - baseLevel);
        }
        else {
            albedo = BaseColorMap2.Sample(texSampler, pin.texCoord);
        }

        if(AlphaCutOff > 0 && albedo.a * Color.a < AlphaCutOff) {
            discard;
        }

        return albedo * Color;
    }
}
