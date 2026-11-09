// Shared by screen space effects that trace against the depth and normal buffers of a [RenderTarget] and
// accumulate over frames. Expects its users to bind their parameters to b0, including BlendFactor, MaxAccumulatedFrames and HighlightThreshold.

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

cbuffer TransformsCam1 : register(b2)
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

cbuffer TransformsCamPrevious : register(b3)
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

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

// t0 is the image (trace, composite) or the raw reflections (temporal).
// t3 is the history (temporal) or the accumulated reflections (composite).
Texture2D<float4> Source : register(t0);
Texture2D<float4> DepthMap : register(t1);
Texture2D<float4> NormalMap : register(t2);
Texture2D<float4> Accumulated : register(t3);
Texture2D<float4> AccumulatedFrameCount : register(t4);
sampler texSampler : register(s0);

static const float BackgroundDepth = 0.99999;
static const int RefinementSteps = 5;

/** NaN or infinity from one pixel would otherwise spread through the bilinear history reads, frame by frame, until
    it floods the image. Replacing it with zero keeps any damage to that one pixel. */
float4 Finite(float4 value)
{
    return any(isnan(value)) || any(isinf(value)) ? 0 : value;
}

float2 UvToNdc(float2 uv)
{
    return float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
}

float2 NdcToUv(float2 ndc)
{
    return float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}

float3 UvDepthToWorld(float2 uv, float depth)
{
    float4 world = mul(float4(UvToNdc(uv), depth, 1.0), ClipSpaceToWorld);
    return world.xyz / world.w;
}

int2 UvToTexel(float2 uv)
{
    return clamp(int2(uv * float2(TargetWidth, TargetHeight)), int2(0, 0), int2((int)TargetWidth, (int)TargetHeight) - 1);
}

float FetchDepth(float2 uv)
{
    return DepthMap.Load(int3(UvToTexel(uv), 0)).r;
}

/** Clear color and shaders without a normal output leave a zero vector, which marks a non-reflecting pixel. */
bool TryFetchNormal(float2 uv, out float3 normal)
{
    normal = Finite(NormalMap.Load(int3(UvToTexel(uv), 0))).xyz;
    float lengthSquared = dot(normal, normal);
    if (lengthSquared < 0.25)
        return false;

    normal /= sqrt(lengthSquared);
    return true;
}

float ViewDepth(float2 uv, float depth)
{
    float4 view = mul(float4(UvToNdc(uv), depth, 1.0), ClipSpaceToCamera);
    return abs(view.z / view.w);
}

float3 EyePosition()
{
    return mul(float4(0, 0, 0, 1), CameraToWorld).xyz;
}

float3 PreviousEyePosition()
{
    return mul(float4(0, 0, 0, 1), PrevCameraToWorld).xyz;
}

/** Returns the previous uv of a world point, with the camera jitter delta removed as in [TXAA]. */
float2 ReprojectToPreviousUv(float3 worldPosition)
{
    float4 previousClip = mul(float4(worldPosition, 1.0), PrevWorldToClipSpace);
    float2 currentJitterUv = float2(CameraToClipSpace._31, -CameraToClipSpace._32) * 0.5;
    float2 previousJitterUv = float2(PrevCameraToClipSpace._31, -PrevCameraToClipSpace._32) * 0.5;
    return NdcToUv(previousClip.xy / previousClip.w) + previousJitterUv - currentJitterUv;
}

bool IsOnScreen(float2 uv)
{
    return uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0;
}

/** Rare ray hits (rough reflections, color bleeding) vanish from most frames, so a clamp against the current
    neighborhood would remove their energy. They skip it and average over fewer frames, which limits their lag. */
static const float MaxFramesForRareHits = 64.0;

/** Returns 1 for pixels that count as emitting light, whose brightness neither occlusion nor reflections should reduce.
    The image holds no emission channel, so HDR brightness stands in for it. */
float GetHighlightProtection(float3 color)
{
    float brightest = max(color.r, max(color.g, color.b));
    return saturate((brightest - HighlightThreshold) / max(HighlightThreshold, 1e-3));
}

struct TemporalOutput
{
    float4 Color : SV_Target0;
    float4 AccumulatedFrames : SV_Target1;
};

/** Returns how many frames the history stands for. Weighting it by frames / (frames + 1) makes a true running
    average that converges, where a fixed blend factor keeps replacing part of the result with new noise forever.
    Disagreement (0..1) is how strongly the current frame contradicts the history. It has to be gradual: an abrupt
    reset would fire on the rare pixel whose few rays all happen to miss, and flash that pixel. */
float GetAccumulatedFrames(float2 uv, float2 previousUv, float confidence, float disagreement)
{
    if (BlendFactor <= 0.0 || confidence < 0.5)
        return 0.0;

    float blend = min(BlendFactor, 0.999);
    float framesWhileChanging = blend / (1.0 - blend);
    float framesWhenSettled = max(MaxAccumulatedFrames, framesWhileChanging);

    float motionInPixels = length((uv - previousUv) * float2(TargetWidth, TargetHeight));
    float change = max(disagreement * disagreement, saturate(motionInPixels * 4.0));
    float limit = lerp(framesWhenSettled, framesWhileChanging, change);
    return min(Finite(AccumulatedFrameCount.SampleLevel(texSampler, previousUv, 0)).r, limit);
}

TemporalOutput MakeTemporalOutput(float3 color, float distanceToCamera, float frames)
{
    TemporalOutput output;
    output.Color = Finite(float4(color, distanceToCamera));
    output.AccumulatedFrames = float4(frames + 1.0, 0, 0, 1);
    return output;
}
