#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float Intensity;
    float Reflectivity;
    float Roughness;
    float MaxDistance;

    float Thickness;
    float MaxSteps;
    float EdgeFade;
    float BlendFactor;

    float NeighborhoodClamp;
    float DepthRejection;
    float Reset;
    float FrameIndex;
    float MaxAccumulatedFrames;
}

#include "shared/screen-space-tracing.hlsl"

/** Tilts the normal by a noise offset that averages out over the temporal accumulation. */
float3 RoughenNormal(float3 normal, float2 pixelSeed)
{
    if (Roughness <= 0.0)
        return normal;

    float3 random = hash32(pixelSeed) * 2.0 - 1.0;
    return normalize(normal + random * Roughness * Roughness);
}

float4 psTrace(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;
    float3 image = Source.SampleLevel(texSampler, uv, 0).rgb;

    float depth = FetchDepth(uv);
    float3 normal;
    if (depth >= BackgroundDepth || !TryFetchNormal(uv, normal))
        return float4(image, 0);

    float3 surface = UvDepthToWorld(uv, depth);
    float3 viewDirection = normalize(surface - EyePosition());
    float2 pixelSeed = uv * float2(TargetWidth, TargetHeight) + fmod(FrameIndex, 1024.0) * float2(17.13, 31.71);

    float3 rayDirection = reflect(viewDirection, RoughenNormal(normal, pixelSeed));
    if (dot(rayDirection, normal) <= 0.0)
        rayDirection = reflect(viewDirection, normal);

    float3 rayStart = surface + normal * 0.001 * length(surface - EyePosition());
    float4 startClip = mul(float4(rayStart, 1.0), WorldToClipSpace);
    float4 endClip = mul(float4(rayStart + rayDirection * MaxDistance, 1.0), WorldToClipSpace);

    // A ray pointing back past the camera has no valid perspective divide at its end.
    const float NearestW = 1e-4;
    if (endClip.w < NearestW)
        endClip = lerp(startClip, endClip, (startClip.w - NearestW) / (startClip.w - endClip.w));

    // uv and z/w are linear along a straight line in post-projection space, so the ray can step uniformly in pixels.
    float3 startScreen = float3(NdcToUv(startClip.xy / startClip.w), startClip.z / startClip.w);
    float3 endScreen = float3(NdcToUv(endClip.xy / endClip.w), endClip.z / endClip.w);
    float3 screenDelta = endScreen - startScreen;

    float rayEnd = 1.0;
    if (screenDelta.x > 0.0) rayEnd = min(rayEnd, (1.0 - startScreen.x) / screenDelta.x);
    if (screenDelta.x < 0.0) rayEnd = min(rayEnd, -startScreen.x / screenDelta.x);
    if (screenDelta.y > 0.0) rayEnd = min(rayEnd, (1.0 - startScreen.y) / screenDelta.y);
    if (screenDelta.y < 0.0) rayEnd = min(rayEnd, -startScreen.y / screenDelta.y);
    if (screenDelta.z > 0.0) rayEnd = min(rayEnd, (1.0 - startScreen.z) / screenDelta.z);

    int stepCount = clamp((int)MaxSteps, 1, 256);
    float stepLength = rayEnd / stepCount;
    float rayPosition = stepLength * hash12(pixelSeed);
    float previousPosition = 0.0;
    float previousViewDepth = ViewDepth(startScreen.xy, startScreen.z);

    bool isHit = false;
    float3 hitScreen = startScreen;

    [loop]
    for (int stepIndex = 0; stepIndex < stepCount; ++stepIndex)
    {
        float3 screen = startScreen + screenDelta * rayPosition;
        float rayViewDepth = ViewDepth(screen.xy, screen.z);
        float sceneDepth = FetchDepth(screen.xy);
        float behindScene = rayViewDepth - ViewDepth(screen.xy, sceneDepth);

        // Scaling with the step keeps grazing rays, which cover much depth per step, from tunneling through.
        float allowedThickness = Thickness + abs(rayViewDepth - previousViewDepth);
        if (sceneDepth < BackgroundDepth && behindScene > 0.0 && behindScene < allowedThickness)
        {
            float low = previousPosition;
            float high = rayPosition;

            [loop]
            for (int refineIndex = 0; refineIndex < RefinementSteps; ++refineIndex)
            {
                float middle = (low + high) * 0.5;
                float3 middleScreen = startScreen + screenDelta * middle;
                float middleSceneDepth = ViewDepth(middleScreen.xy, FetchDepth(middleScreen.xy));
                if (ViewDepth(middleScreen.xy, middleScreen.z) > middleSceneDepth)
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }

            hitScreen = startScreen + screenDelta * high;
            isHit = true;
            break;
        }

        previousPosition = rayPosition;
        previousViewDepth = rayViewDepth;
        rayPosition += stepLength;
    }

    if (!isHit)
        return float4(image, 0);

    // The depth test also accepts the back of a surface, whose color was never rendered.
    float3 hitNormal;
    if (TryFetchNormal(hitScreen.xy, hitNormal) && dot(hitNormal, rayDirection) > 0.0)
        return float4(image, 0);

    float hitDepth = FetchDepth(hitScreen.xy);
    float hitDistance = distance(rayStart, UvDepthToWorld(hitScreen.xy, hitDepth));

    float2 distanceToEdge = min(hitScreen.xy, 1.0 - hitScreen.xy);
    float edgeFactor = saturate(min(distanceToEdge.x, distanceToEdge.y) / max(EdgeFade, 1e-4));
    float distanceFactor = 1.0 - saturate(hitDistance / max(MaxDistance, 1e-4));
    float confidence = edgeFactor * sqrt(distanceFactor);

    float3 reflection = Source.SampleLevel(texSampler, hitScreen.xy, 0).rgb;

    // Blending misses toward the image makes a separate confidence channel unnecessary: fading out needs no history reset.
    return float4(lerp(image, reflection, confidence), hitDistance);
}

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

TemporalOutput psTemporal(vsOutput input)
{
    float2 uv = input.texCoord;
    float2 texel = float2(1.0 / TargetWidth, 1.0 / TargetHeight);
    float4 raw = Source.SampleLevel(texSampler, uv, 0);

    float depth = FetchDepth(uv);
    if (depth >= BackgroundDepth)
        return MakeTemporalOutput(raw.rgb, 0, 0);

    float3 surface = UvDepthToWorld(uv, depth);
    float3 viewRay = surface - EyePosition();

    // A mirror image moves like an object behind the mirror, not with the surface showing it.
    // Rough reflections have no single depth, so they blend toward moving with the surface.
    float virtualDistance = raw.a * (1.0 - saturate(Roughness * 2.0));
    float3 virtualPoint = surface + normalize(viewRay) * virtualDistance;
    float2 reflectionUv = ReprojectToPreviousUv(virtualPoint);
    float2 surfaceUv = ReprojectToPreviousUv(surface);

    float3 centerYCoCg = RgbToYCoCg(raw.rgb);
    float3 boxMin = centerYCoCg;
    float3 boxMax = centerYCoCg;

    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            float3 neighbor = RgbToYCoCg(Source.SampleLevel(texSampler, uv + float2(x, y) * texel, 0).rgb);
            boxMin = min(boxMin, neighbor);
            boxMax = max(boxMax, neighbor);
        }
    }

    float3 boxCenter = (boxMin + boxMax) * 0.5;
    // The minimum keeps a converged history from snapping to the current frame where all neighbors happen to agree.
    float3 boxExtent = (boxMax - boxMin) * 0.5 * (1.0 + NeighborhoodClamp) + 0.02;

    float3 history = Accumulated.SampleLevel(texSampler, reflectionUv, 0).rgb;
    float3 historyYCoCg = RgbToYCoCg(history);
    float3 clampedYCoCg = clamp(historyYCoCg, boxCenter - boxExtent, boxCenter + boxExtent);
    float disagreement = saturate(length(historyYCoCg - clampedYCoCg) / 0.05);
    history = YCoCgToRgb(clampedYCoCg);

    // Half floats hold nonlinear depth poorly but give a camera distance constant relative precision.
    float storedDistance = Accumulated.SampleLevel(texSampler, surfaceUv, 0).a;
    float expectedDistance = distance(surface, PreviousEyePosition());
    float relativeGap = abs(storedDistance - expectedDistance) / max(expectedDistance, 1e-4);

    float confidence = 1.0 - saturate(relativeGap / max(DepthRejection, 1e-5));
    if (!IsOnScreen(reflectionUv) || !IsOnScreen(surfaceUv) || storedDistance <= 0.0 || Reset > 0.5)
        confidence = 0.0;

    float frames = GetAccumulatedFrames(uv, surfaceUv, confidence, disagreement);
    float3 resolved = lerp(raw.rgb, history, frames / (frames + 1.0));
    return MakeTemporalOutput(resolved, length(viewRay), frames);
}

float4 psComposite(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;
    float4 image = Source.SampleLevel(texSampler, uv, 0);

    float depth = FetchDepth(uv);
    float3 normal;
    if (depth >= BackgroundDepth || !TryFetchNormal(uv, normal))
        return image;

    float3 viewDirection = normalize(EyePosition() - UvDepthToWorld(uv, depth));
    float grazing = 1.0 - saturate(dot(normal, viewDirection));
    float grazing2 = grazing * grazing;
    float fresnel = Reflectivity + (1.0 - Reflectivity) * grazing2 * grazing2 * grazing;

    float3 reflection = Accumulated.SampleLevel(texSampler, uv, 0).rgb;
    return float4(lerp(image.rgb, reflection, saturate(fresnel * Intensity)), image.a);
}
