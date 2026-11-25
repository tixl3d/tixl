#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"
#include "shared/velocity-functions.hlsl"

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
    float UseVertexColor;

    /** 1 with depth writing: only then does the normal belong to this pixel's depth. Unset reads 0. */
    float WritesDepth;
};

/** Last frame's transforms of this draw, for the velocity target. */
cbuffer PreviousTransforms : register(b2)
{
    float4x4 PrevCameraToClipSpace;
    float4x4 PrevClipSpaceToCamera;
    float4x4 PrevWorldToCamera;
    float4x4 PrevCameraToWorld;
    float4x4 PrevWorldToClipSpace;
    float4x4 PrevClipSpaceToWorld;
    float4x4 PrevObjectToWorld;
    float4x4 PrevWorldToObject;
    float4x4 PrevObjectToCamera;
    float4x4 PrevObjectToClipSpace;
};


struct psInput
{
    float2 texCoord : TEXCOORD;
    float4 pixelPosition : SV_POSITION;
    float3 normal : POSITION;
    float3 colorRGB : COLOR;
    float3 normalInWorld : NORMAL;
    float4 clipPosition : CLIP_POSITION;
    float4 previousClipPosition : PREVIOUS_CLIP_POSITION;
};

struct psOutput
{
    float4 Color : SV_Target0;

    // Unlit surfaces still occlude and reflect, so AO and reflections need their geometry. Without depth writing the
    // normal behind is kept, as the background would otherwise be shaded with this surface's direction.
    float4 Normal : SV_Target1;
    float4 Velocity : SV_Target2;
};

sampler texSampler : register(s0);

StructuredBuffer<PbrVertex> PbrVertices : register(t0);
StructuredBuffer<int3> FaceIndices : register(t1);

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
    output.clipPosition = posInClipSpace;
    output.previousClipPosition = mul(posInObject, PrevObjectToClipSpace);
    output.normalInWorld = mul(float4(vertex.Normal, 0), ObjectToWorld).xyz;

    output.normal = vertex.Normal;
    output.colorRGB = vertex.ColorRGB;
    float2 uv = vertex.TexCoord;
    output.texCoord = float2(uv.x , 1- uv.y);
    return output;
}


float4 ComputeColor(psInput pin)
{
    if(UseCubeMap > 0.5) {
        
        uint width, height, levels;
        CubeMap.GetDimensions(0, width, height, levels);

        float mipLevel = BlurLevel * levels;
        int baseLevel = (int)mipLevel;

        float4 c1 = CubeMap.SampleLevel(texSampler, pin.normal.xyz, baseLevel);
        float4 c2 = CubeMap.SampleLevel(texSampler, pin.normal.xyz, baseLevel + 1);
        float4 albedo = lerp(c1, c2, mipLevel - baseLevel);
        return albedo * Color;        
    }
    else 
    {
        uint width, height, levels;
        BaseColorMap2.GetDimensions(0, width, height, levels);
        float mipLevel = BlurLevel * levels;
        int baseLevel = (int)mipLevel;

        float4 albedo = 0;
        if(BlurLevel > 0) 
        {
            float4 c1 = BaseColorMap2.SampleLevel(texSampler, pin.texCoord, baseLevel);
            float4 c2 = BaseColorMap2.SampleLevel(texSampler, pin.texCoord, baseLevel + 1);
            albedo = lerp(c1, c2, mipLevel - baseLevel);
        }
        else {
            albedo = BaseColorMap2.Sample(texSampler, pin.texCoord);
        }

        if(AlphaCutOff > 0 && albedo.a * Color.a < AlphaCutOff) {
            discard;
        }

        float4 vertexColor = UseVertexColor > 0.5 ? float4(pin.colorRGB, 1) : 1;

        return albedo * Color * vertexColor;
    }
}

psOutput psMain(psInput pin, bool isFrontFace : SV_IsFrontFace)
{
    psOutput output;
    output.Color = ComputeColor(pin);

    // Back faces of meshes drawn without culling face away from the viewer.
    float3 normal = normalize(pin.normalInWorld) * (isFrontFace ? 1 : -1);
    output.Normal = WritesDepth > 0.5 ? float4(normal, output.Color.a) : 0;
    output.Velocity = EncodeVelocity(pin.clipPosition, pin.previousClipPosition, CameraToClipSpace, PrevCameraToClipSpace);
    return output;
}
