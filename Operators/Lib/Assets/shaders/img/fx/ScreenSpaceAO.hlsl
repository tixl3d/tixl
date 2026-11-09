#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float Intensity;
    float Radius;
    float Thickness;
    float RaysPerPixel;

    float StepsPerRay;
    float BlendFactor;
    float NeighborhoodClamp;
    float DepthRejection;

    float Reset;
    float FrameIndex;
    float MaxAccumulatedFrames;
    float ColorBleeding;
    float HighlightThreshold;
}

#include "shared/screen-space-tracing.hlsl"

/** Shaders without a normal output still receive occlusion, with the facets of their depth surface. */
float3 ReconstructNormal(float2 uv, float3 surface)
{
    float2 texel = float2(1.0 / TargetWidth, 1.0 / TargetHeight);
    float3 right = UvDepthToWorld(uv + float2(texel.x, 0), FetchDepth(uv + float2(texel.x, 0))) - surface;
    float3 left = surface - UvDepthToWorld(uv - float2(texel.x, 0), FetchDepth(uv - float2(texel.x, 0)));
    float3 down = UvDepthToWorld(uv + float2(0, texel.y), FetchDepth(uv + float2(0, texel.y))) - surface;
    float3 up = surface - UvDepthToWorld(uv - float2(0, texel.y), FetchDepth(uv - float2(0, texel.y)));

    // The shorter difference stays on this surface at silhouettes.
    float3 horizontal = dot(right, right) < dot(left, left) ? right : left;
    float3 vertical = dot(down, down) < dot(up, up) ? down : up;

    float3 normal = normalize(cross(horizontal, vertical));
    return dot(normal, EyePosition() - surface) < 0.0 ? -normal : normal;
}

/** Traced light from what the rays hit in rgb, share of unblocked rays in alpha. */
float4 psTrace(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;
    float depth = FetchDepth(uv);
    if (depth >= BackgroundDepth)
        return float4(0, 0, 0, 1);

    float3 surface = UvDepthToWorld(uv, depth);
    float3 normal;
    if (!TryFetchNormal(uv, normal))
        normal = ReconstructNormal(uv, surface);

    float selfHitBias = 0.002 * length(surface - EyePosition());
    float3 rayStart = surface + normal * selfHitBias;
    float2 pixelSeed = uv * float2(TargetWidth, TargetHeight) + fmod(FrameIndex, 1024.0) * float2(17.13, 31.71);

    int rayCount = clamp((int)RaysPerPixel, 1, 32);
    int stepCount = clamp((int)StepsPerRay, 1, 64);
    float occlusion = 0.0;
    float3 bleeding = 0.0;

    [loop]
    for (int rayIndex = 0; rayIndex < rayCount; ++rayIndex)
    {
        float3 random = hash32(pixelSeed + rayIndex * float2(7.31, 3.17));

        // Adding a uniform point on the unit sphere to the normal gives a cosine weighted hemisphere direction.
        float height = random.x * 2.0 - 1.0;
        float angle = random.y * 6.2831853;
        float ring = sqrt(saturate(1.0 - height * height));
        float3 rayDirection = normalize(normal + float3(ring * cos(angle), ring * sin(angle), height) + 1e-4);

        [loop]
        for (int stepIndex = 0; stepIndex < stepCount; ++stepIndex)
        {
            // Squared spacing puts more samples near the surface, where contact shadows need the precision.
            float progress = (stepIndex + random.z) / stepCount;
            progress *= progress;

            float4 clip = mul(float4(rayStart + rayDirection * Radius * progress, 1.0), WorldToClipSpace);
            if (clip.w <= 0.0)
                break;

            float2 sampleUv = NdcToUv(clip.xy / clip.w);
            if (!IsOnScreen(sampleUv))
                break;

            float sceneDepth = FetchDepth(sampleUv);
            if (sceneDepth >= BackgroundDepth)
                continue;

            float behindScene = ViewDepth(sampleUv, clip.z / clip.w) - ViewDepth(sampleUv, sceneDepth);
            if (behindScene > selfHitBias && behindScene < Thickness)
            {
                float hitWeight = 1.0 - progress;
                occlusion += hitWeight;
                bleeding += Finite(Source.SampleLevel(texSampler, sampleUv, 0)).rgb * hitWeight;
                break;
            }
        }
    }

    return float4(bleeding / rayCount, 1.0 - occlusion / rayCount);
}

/** Color target holds visibility in rgb, so that the Occlusion output is plain grayscale. Bleeding travels with the frame count. */
TemporalOutput psTemporal(vsOutput input)
{
    float2 uv = input.texCoord;
    float2 texel = float2(1.0 / TargetWidth, 1.0 / TargetHeight);
    float4 raw = Finite(Source.SampleLevel(texSampler, uv, 0));

    TemporalOutput output;
    float depth = FetchDepth(uv);
    if (depth >= BackgroundDepth)
    {
        output.Color = float4(1, 1, 1, 0);
        output.AccumulatedFrames = float4(1, 0, 0, 0);
        return output;
    }

    float3 surface = UvDepthToWorld(uv, depth);
    float2 previousUv = ReprojectToPreviousUv(surface);

    float4 sum = 0.0;
    float4 sumSquared = 0.0;

    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            float4 neighbor = Finite(Source.SampleLevel(texSampler, uv + float2(x, y) * texel, 0));
            sum += neighbor;
            sumSquared += neighbor * neighbor;
        }
    }

    float4 mean = sum / 9.0;
    float4 deviation = sqrt(max(sumSquared / 9.0 - mean * mean, 0.0));

    // Single rays differ strongly between neighbors, which would make a min/max box accept almost any history.
    // The minimum is about three standard deviations of the mean of the neighborhood's few rays. Without it a
    // converged history would snap to the current frame where all neighbors happen to miss.
    float allowedDeviation = max(deviation.a * (1.0 + NeighborhoodClamp * 3.0), 0.25);

    float4 historyColor = Finite(Accumulated.SampleLevel(texSampler, previousUv, 0));
    float3 historyBleeding = Finite(AccumulatedFrameCount.SampleLevel(texSampler, previousUv, 0)).gba;
    float historyVisibility = clamp(historyColor.r, mean.a - allowedDeviation, mean.a + allowedDeviation);

    float expectedDistance = distance(surface, PreviousEyePosition());
    float relativeGap = abs(historyColor.a - expectedDistance) / max(expectedDistance, 1e-4);

    float confidence = 1.0 - saturate(relativeGap / max(DepthRejection, 1e-5));
    if (!IsOnScreen(previousUv) || historyColor.a <= 0.0 || Reset > 0.5)
        confidence = 0.0;

    float frames = GetAccumulatedFrames(uv, previousUv, confidence, saturate(abs(historyColor.r - mean.a) / 0.25));
    float bleedingFrames = min(frames, MaxFramesForRareHits);

    // Newly revealed pixels have no history to hide their noise, so they start from the neighborhood average.
    float4 current = lerp(mean, raw, saturate(frames * 0.25));
    float visibility = lerp(current.a, historyVisibility, frames / (frames + 1.0));
    float3 bleeding = lerp(current.rgb, historyBleeding, bleedingFrames / (bleedingFrames + 1.0));

    output.Color = Finite(float4(visibility, visibility, visibility, length(surface - EyePosition())));
    output.AccumulatedFrames = Finite(float4(frames + 1.0, bleeding));
    return output;
}

float4 psComposite(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;
    float4 image = Source.SampleLevel(texSampler, uv, 0);
    if (FetchDepth(uv) >= BackgroundDepth)
        return image;

    float visibility = Finite(Accumulated.SampleLevel(texSampler, uv, 0)).r;
    float3 bleeding = Finite(AccumulatedFrameCount.SampleLevel(texSampler, uv, 0)).gba;

    // There is no albedo buffer, so the lit color stands in for it. The minimum keeps dark surfaces from going bright.
    float3 albedo = saturate(image.rgb / max(max(image.r, max(image.g, image.b)), 0.1));
    float shading = lerp(pow(saturate(visibility), max(Intensity, 0.0)), 1.0, GetHighlightProtection(image.rgb));
    float3 shaded = image.rgb * shading;
    return float4(shaded + bleeding * albedo * ColorBleeding, image.a);
}
