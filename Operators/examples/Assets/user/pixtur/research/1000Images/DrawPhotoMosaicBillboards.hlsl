#include "shared/point.hlsl"
#include "shared/hash-functions.hlsl"
#include "shared/quat-functions.hlsl"

static const float3 Corners[] =
    {
        float3(-1, -1, 0),
        float3(1, -1, 0),
        float3(1, 1, 0),
        float3(1, 1, 0),
        float3(-1, 1, 0),
        float3(-1, -1, 0),
};

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
    float Scale;
};


struct psInput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    int arrayIndex: INDEX;
};

sampler texSampler : register(s0);
sampler pointSampler : register(s1);

StructuredBuffer<LegacyPoint> Points : register(t0);
Texture2DArray<float4> SpriteTexture : register(t1);
Texture2D<float4> FxTexture : register(t2);
Texture3D<float> IndexFromColorLookUp : register(t3);

static const float3x3 invB = {0.4121656120, 0.2118591070, 0.0883097947, 0.5362752080, 0.6807189584, 0.2818474174, 0.0514575653, 0.1074065790, 0.6302613616};
static const float3x3 invA = {0.2104542553, 1.9779984951, 0.0259040371, 0.7936177850, -2.4285922050, 0.7827717662,-0.0040720468, 0.4505937099, -0.8086757660};

inline float3 RgbToLCh(float3 col) {
    col = mul(col, invB);
    col= mul((sign(col) * pow(abs(col), 0.3333333333333)), invA);    

    float3 polar = 0;
    polar.x = col.x;
    polar.y = sqrt(col.y * col.y + col.z * col.z);
    polar.z = atan2(col.z, col.y);
    polar.z= polar.z / (2 * PI) + 0.5;
    return polar;
}

psInput vsMain(uint id: SV_VertexID)
{
    psInput output;

    uint particleCount, stride;
    Points.GetDimensions(particleCount, stride);

    uint quadIndex = id % 6;
    uint pointId = id / 6;
    float3 cornerFactors = Corners[quadIndex];

    LegacyPoint p = Points[pointId]; 

    float f = pointId / (float)particleCount;
    output.texCoord = (cornerFactors.xy * float2(1, -1) * 0.5 + 0.5);

    float4 posInObject = float4(p.Position, 1);
    float4 quadPosInCamera = mul(posInObject, ObjectToCamera);
    float4 posInCamera = mul(posInObject, ObjectToCamera);

    float4 centerPos = mul(float4(quadPosInCamera.xyz, 1), CameraToClipSpace);
    centerPos.xyz /= centerPos.w;

    // Sample reference image
    // Note: We need to sample an explicity mip level, because the texture scale can't be be 
    // computed in the vertex shader
    float4 fxColor = FxTexture.SampleLevel(texSampler, (centerPos.xy * float2(1, -1) + 1) / 2, 0);
    float3 lch = RgbToLCh(fxColor.rgb);
    lch.x += hash11u(pointId) * 0.1;   // Add some variation to "dither"
    output.arrayIndex = IndexFromColorLookUp.SampleLevel(pointSampler, lch,0);

    float hideUndefinedPoints = isnan(p.W) ? 0 : 1;
    float computedScale = hideUndefinedPoints * Scale;

    float3 axis = ( cornerFactors ) * 0.010;
    axis = qRotateVec3(axis, p.Rotation) * computedScale;
    output.position = mul(posInObject + float4(axis, 0), ObjectToClipSpace);
    return output;
}

float4 psMain(psInput input) : SV_TARGET
{
    return SpriteTexture.Sample(texSampler, float3(input.texCoord, input.arrayIndex));
}
