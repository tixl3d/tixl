//#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    // TBD Transform const buffer
}

cbuffer ParamConstants : register(b1)
{
    float SampleCount;
    float Strength;
    float Clamp_;
}

cbuffer TimeConstants : register(b2)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

cbuffer Resolution : register(b3)
{
    float TargetWidth;
    float TargetHeight;
}

cbuffer TransformsCam1 : register(b4)
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

cbuffer TransformsCamPrevious : register(b5)
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

cbuffer AdditionalTransformParams : register(b6)
{
    float3 AdditionalMotionOffset;
    // float4x4 AdditionalMotionOffset;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
Texture2D<float4> DepthMap : register(t1);
sampler texSampler : register(s0);

float IsBetween(float value, float low, float high)
{
    return (value >= low && value <= high) ? 1 : 0;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float maxVelocity = Clamp_ / 100;

    int samples = (int)clamp(SampleCount + 0.5, 1, 32);
    // float displaceMapWidth, displaceMapHeight;

    float2 uv = psInput.texCoord;
    float4 c = DepthMap.Sample(texSampler, uv);

    float depth = DepthMap.Sample(texSampler, uv).r;
    depth = min(depth, 0.999);

    float4 viewTFragPos = float4(-uv.x * 2.0 + 1.0, uv.y * 2.0 - 1.0, depth, 1.0);
    float4 worldTFragPos = mul(viewTFragPos, ClipSpaceToWorld); // viewToWorld?
    worldTFragPos /= worldTFragPos.x;

    // float4x4 test = mul(AdditionalMotionOffset, PrevWorldToClipSpace);
    float4x4 test = PrevWorldToClipSpace;
    test._m00 += AdditionalMotionOffset.x;
    test._m33 += AdditionalMotionOffset.z;

    float4 viewTPreviousFragPos = mul(worldTFragPos, test); //  previousWorldToView
    viewTPreviousFragPos /= viewTPreviousFragPos.w;

    float2 velocity = (viewTFragPos.xy - viewTPreviousFragPos.xy) * Strength / 100;

    velocity.x = -velocity.x;
    if (abs(velocity.x) < 0.0001)
        velocity.x = 0.0;
    if (abs(velocity.y) < 0.0001)
        velocity.y = 0.0;

    float l = length(velocity);
    if (l > 0 && l > maxVelocity)
        velocity *= maxVelocity / l;

    float2 dir = velocity * 10.0 / samples;
    float2 pos = dir;
    float totalWeight = 1;
    c = 0;

    float weight = 1;
    for (int i = 0; i < samples; ++i)
    {
        c += Image.SampleLevel(texSampler, uv + pos, 0) * weight;
        c += Image.SampleLevel(texSampler, uv - pos, 0) * weight;
        pos += dir;
        totalWeight += 2 * weight;
    }
    c.rgb /= totalWeight;
    c.a = 1.0;
    return c;
}