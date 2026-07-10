cbuffer ParamConstants : register(b0)
{
    float BlendFactor;        // History weight in steady state (0..1, ~0.9).
    float NeighborhoodClamp;  // Widens the YCoCg clamp box; 0 = tight (sharp, more flicker), 1 = loose (stable, more ghosting).
    float MotionThreshold;    // Reprojected motion (in uv) above this discards history -> clears on large jumps.
    float DepthRejection;     // World-space gap between the current surface and the one stored last frame above this rejects history (disocclusion -> hard edge).
    float Reset;              // >0.5 ignores history for this frame.
}

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

Texture2D<float4> Image : register(t0);
Texture2D<float4> DepthMap : register(t1);
Texture2D<float4> History : register(t2);
sampler texSampler : register(s0);

// YCoCg is the standard space for TAA neighborhood clamping: chroma separates from luma
// so the clamp box bounds perceived color rather than raw RGB, which suppresses ghosting
// far better while keeping edges crisp.
float3 RgbToYCoCg(float3 c)
{
    return float3(dot(c, float3(0.25, 0.5, 0.25)),
                  dot(c, float3(0.5, 0.0, -0.5)),
                  dot(c, float3(-0.25, 0.5, -0.25)));
}

float3 YCoCgToRgb(float3 c)
{
    return float3(c.x + c.y - c.z,
                  c.x + c.z,
                  c.x - c.y - c.z);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float2 texel = float2(1.0 / TargetWidth, 1.0 / TargetHeight);

    float4 current = Image.SampleLevel(texSampler, uv, 0);
    float depth = DepthMap.SampleLevel(texSampler, uv, 0).r;

    // Reconstruct this pixel's world position from depth + the current camera. Clip-space
    // orientation matches TiXL's GetCamTransformBuffer / Default2-vs.hlsl.
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 worldPos = mul(float4(ndc, depth, 1.0), ClipSpaceToWorld);
    worldPos /= worldPos.w;

    // Project that world point through the previous camera to find where it was on screen.
    float4 prevClip = mul(worldPos, PrevWorldToClipSpace);
    prevClip /= prevClip.w;
    float2 prevUv = float2(prevClip.x * 0.5 + 0.5, 0.5 - prevClip.y * 0.5);

    // Un-jitter the reprojection so the accumulated history stays in stable (jitter-free) pixel
    // alignment: with a static camera prevUv then equals uv exactly, avoiding the per-frame
    // fractional resampling that would otherwise blur the history over time. The sub-pixel
    // jitter lives in the projection matrices' NDC translation terms (same slots as LensShift;
    // a constant lens shift cancels out here). NDC y is up while texel y is down, hence the flip.
    float2 currentJitterUv = float2(CameraToClipSpace._31, -CameraToClipSpace._32) * 0.5;
    float2 previousJitterUv = float2(PrevCameraToClipSpace._31, -PrevCameraToClipSpace._32) * 0.5;
    // Remove the jitter *delta*: with a static camera the fetch then lands exactly on uv — the
    // fixed point that keeps the accumulation stable while the zero-mean jittered current frame
    // blends in as sub-pixel supersamples. (Correcting with only one frame's jitter shifts the
    // fetch by the other frame's jitter every frame — the whole image vibrates.)
    prevUv += previousJitterUv - currentJitterUv;

    float2 velocity = uv - prevUv;
    //return float4((velocity * 1000) + 0.5,0,1);
    //return float4(velocity * TargetWidth * 0.25 + 0.5, 0, 1);

    // YCoCg AABB of the 3x3 current neighborhood; history is clamped into this box, which is
    // the primary ghost suppressor for moving content within the screen.
    float3 centerYCoCg = RgbToYCoCg(current.rgb);
    float3 boxMin = centerYCoCg;
    float3 boxMax = centerYCoCg;

    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            if (x == 0 && y == 0)
                continue;

            float3 neighbor = RgbToYCoCg(Image.SampleLevel(texSampler, uv + float2(x, y) * texel, 0).rgb);
            boxMin = min(boxMin, neighbor);
            boxMax = max(boxMax, neighbor);
        }
    }

    float3 boxCenter = (boxMin + boxMax) * 0.5;
    float3 boxExtent = (boxMax - boxMin) * 0.5 * (1.0 + NeighborhoodClamp);
    boxMin = boxCenter - boxExtent;
    boxMax = boxCenter + boxExtent;

    // History carries the previous resolved color in rgb and that frame's scene depth in alpha.
    float4 historySample = History.SampleLevel(texSampler, prevUv, 0);

    //return lerp(Image.SampleLevel(texSampler, uv, 0), historySample, BlendFactor); // <--- Debug test

    float3 history = YCoCgToRgb(clamp(RgbToYCoCg(historySample.rgb), boxMin, boxMax));

    // Confidence in the history sample. Anything that makes reprojection unreliable drives
    // this toward 0, which falls back to the current frame and prevents trailing.
    float confidence = 1.0;

    // Offscreen: nothing was rendered there last frame.
    if (prevUv.x < 0.0 || prevUv.x > 1.0 || prevUv.y < 0.0 || prevUv.y > 1.0)
        confidence = 0.0;

    // Large reprojected motion -> clear rather than smear.
    confidence *= 1.0 - saturate(length(velocity) / max(MotionThreshold, 1e-5));

    // Depth disocclusion: reconstruct the world point the previous frame actually recorded at
    // prevUv (its stored depth + the previous camera). If it sits far from the current surface,
    // a different/occluding surface was visible there last frame, so history is invalid and we
    // reject it for a hard edge.
    float4 prevWorld = mul(float4(prevClip.xy, historySample.a, 1.0), PrevClipSpaceToWorld);
    prevWorld /= prevWorld.w;
    float surfaceGap = distance(worldPos.xyz, prevWorld.xyz);
    confidence *= 1.0 - saturate(surfaceGap / max(DepthRejection, 1e-5));

    if (Reset > 0.5)
        confidence = 0.0;

    float weight = BlendFactor * confidence;
    float3 resolved = lerp(current.rgb, history, weight);

    // Store this frame's depth in alpha so next frame's disocclusion test can read it.
    return float4(resolved, depth);
}
