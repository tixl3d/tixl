// Motion blur reconstructed from per-pixel motion, after "A Reconstruction Filter for Plausible Motion Blur"
// (McGuire et al. 2012) with the jittered sampling of Jimenez (Next Generation Post Processing in Call of Duty, 2014).
//
// psPrepare      full resolution: blur radius in pixels (rg) and view depth (b) of each pixel
// psTileMax      one pixel per tile: the largest radius in the tile
// psNeighborMax  one pixel per tile: the largest radius of the tile and its 8 neighbors
// psGather       full resolution: samples along the largest nearby motion, so fast objects smear over still ones
//
// The tile is at least as large as MaxBlur, so the 3x3 tile neighborhood holds every pixel whose blur can reach this one.

#include "shared/velocity-functions.hlsl"

cbuffer Params : register(b0)
{
    /** The shutter's share of the average frame, divided by this frame's share, so frame-time jitter cancels. */
    float ShutterScale;
    float MaxBlur;
    float SampleCount;
    float TileSize;

    float2 Resolution;
    float2 TileCount;
}

cbuffer TransformsCam : register(b1)
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

cbuffer TransformsCamPrevious : register(b2)
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
    float2 uv : TEXCOORD0;
};

Texture2D<float4> Image : register(t0);
Texture2D<float4> DepthMap : register(t1);

/** Unbound it reads 0, meaning no motion known; the camera motion is used instead. */
Texture2D<float4> VelocityMap : register(t2);

/** psPrepare's result: radius in pixels (rg), view depth (b). */
Texture2D<float4> Prepared : register(t3);

/** The tile maximum (read by psNeighborMax) or the neighborhood maximum (read by psGather). */
Texture2D<float4> Tiles : register(t4);

sampler ClampedSampler : register(s0);

/** Depth difference, relative to the view depth, over which a sample changes from in front to behind. */
static const float SoftDepthRange = 0.02;

/** Radii are at least half a pixel, the extent of the pixel itself. */
static const float MinRadius = 0.5;

float2 UvToNdc(float2 uv)
{
    return float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
}

float2 NdcToUv(float2 ndc)
{
    return float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}

/** An unbound camera buffer reads zeros. */
bool HasCamera()
{
    return abs(CameraToClipSpace._11) > 1e-9 && abs(PrevCameraToClipSpace._11) > 1e-9;
}

int2 ScaleTexel(int2 texel, Texture2D<float4> map)
{
    uint width, height;
    map.GetDimensions(width, height);
    return clamp(int2(float2(texel + 0.5) * float2(width, height) / Resolution), 0, int2(width, height) - 1);
}

float Noise(float2 pixel)
{
    // Interleaved gradient noise: neighbors differ strongly, so few samples give fine grain instead of bands.
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

//-------------------------------------------------------------------------

float4 psPrepare(vsOutput input) : SV_TARGET
{
    int2 texel = int2(input.position.xy);
    float2 uv = (texel + 0.5) / Resolution;
    float depth = DepthMap.Load(int3(ScaleTexel(texel, DepthMap), 0)).r;

    float2 velocity;
    bool hasVelocity = TryDecodeVelocity(VelocityMap.Load(int3(ScaleTexel(texel, VelocityMap), 0)), depth, velocity);

    bool hasCamera = HasCamera();
    if (!hasVelocity)
    {
        velocity = 0;
        if (hasCamera)
        {
            // Static geometry: where this point was through last frame's camera. The background reprojects as far away.
            float4 world = mul(float4(UvToNdc(uv), min(depth, 0.99999), 1.0), ClipSpaceToWorld);
            float4 previousClip = mul(float4(world.xyz / world.w, 1.0), PrevWorldToClipSpace);
            if (previousClip.w > 1e-6)
            {
                float2 previousUv = NdcToUv(previousClip.xy / previousClip.w)
                                    + JitterUv(PrevCameraToClipSpace) - JitterUv(CameraToClipSpace);
                velocity = uv - previousUv;
            }
        }
    }

    // The blur covers the path during the open shutter, centered on the frame's moment: half of it to each side.
    float2 radius = velocity * Resolution * ShutterScale * 0.5;
    float radiusLength = sqrt(dot(radius, radius));
    if (radiusLength > MaxBlur)
        radius *= MaxBlur / radiusLength;

    float viewDepth;
    if (hasCamera)
    {
        float4 view = mul(float4(UvToNdc(uv), depth, 1.0), ClipSpaceToCamera);
        viewDepth = abs(view.z / view.w);
    }
    else
    {
        // Without a camera only the order matters; this grows with the distance for perspective projections.
        viewDepth = 1.0 / max(1.0001 - depth, 1e-4);
    }

    return float4(radius, viewDepth, 1);
}

float4 psTileMax(vsOutput input) : SV_TARGET
{
    int2 tile = int2(input.position.xy);
    int tileSize = (int)TileSize;
    int2 first = tile * tileSize;
    int2 last = min(first + tileSize, int2(Resolution)) - 1;

    float2 largest = 0;
    float largestSquared = 0;
    for (int y = first.y; y <= last.y; ++y)
    {
        for (int x = first.x; x <= last.x; ++x)
        {
            float2 radius = Prepared.Load(int3(x, y, 0)).rg;
            float lengthSquared = dot(radius, radius);
            if (lengthSquared > largestSquared)
            {
                largestSquared = lengthSquared;
                largest = radius;
            }
        }
    }
    return float4(largest, 0, 1);
}

float4 psNeighborMax(vsOutput input) : SV_TARGET
{
    int2 tile = int2(input.position.xy);
    int2 lastTile = int2(TileCount) - 1;

    float2 largest = 0;
    float largestSquared = 0;

    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            float2 radius = Tiles.Load(int3(clamp(tile + int2(x, y), 0, lastTile), 0)).rg;
            float lengthSquared = dot(radius, radius);
            if (lengthSquared > largestSquared)
            {
                largestSquared = lengthSquared;
                largest = radius;
            }
        }
    }
    return float4(largest, 0, 1);
}

/** 1 if a is at least as close as b, fading to 0 over the soft range. */
float SoftDepthCompare(float a, float b)
{
    return saturate(1.0 - (a - b) / (SoftDepthRange * max(b, 1e-4)));
}

/** 1 within the radius: a point spreads evenly over its path. Softened by a pixel at the end. */
float Covers(float distance, float radius)
{
    return saturate(radius - distance + 0.5);
}

float4 psGather(vsOutput input) : SV_TARGET
{
    float2 pixel = input.position.xy;
    int2 texel = int2(pixel);
    float4 color = Image.Load(int3(texel, 0));

    // A jittered tile lookup hides the tile borders where the dominant motion changes.
    float noise = Noise(pixel);
    int2 jitteredTexel = texel + int2((float2(noise, frac(noise * 7.13)) - 0.5) * TileSize * 0.5);
    int2 tile = clamp(jitteredTexel / (int)TileSize, 0, int2(TileCount) - 1);
    float2 dominant = Tiles.Load(int3(tile, 0)).rg;
    float dominantLength = length(dominant);
    if (dominantLength < MinRadius)
        return color;

    float4 center = Prepared.Load(int3(texel, 0));
    float centerRadius = max(length(center.rg), MinRadius);
    float centerDepth = center.b;

    // Weights follow how a moving point spreads its color: evenly over its path, so 1 / radius per pixel.
    // A sample in front of this pixel contributes where its own blur reaches here; one behind shows through where
    // this pixel's blur makes it transparent.
    int sampleCount = clamp((int)(SampleCount + 0.5), 2, 64);
    float spacing = 2.0 * dominantLength / sampleCount;
    float totalWeight = 1.0 / centerRadius;
    float3 sum = color.rgb * totalWeight;

    for (int i = 0; i < sampleCount; ++i)
    {
        float t = -1.0 + 2.0 * (i + noise) / sampleCount;
        float2 offset = dominant * t;
        float sampleDistance = abs(t) * dominantLength;
        if (sampleDistance < MinRadius)
            continue;

        int2 sampleTexel = clamp(int2(pixel + offset), 0, int2(Resolution) - 1);
        float4 prepared = Prepared.Load(int3(sampleTexel, 0));
        float sampleRadius = max(length(prepared.rg), MinRadius);
        float sampleDepth = prepared.b;

        float inFront = SoftDepthCompare(sampleDepth, centerDepth);
        float behind = SoftDepthCompare(centerDepth, sampleDepth);
        float weight = spacing * (inFront * Covers(sampleDistance, sampleRadius) / sampleRadius
                                  + behind * Covers(sampleDistance, centerRadius) / centerRadius);

        float2 sampleUv = (pixel + offset) / Resolution;
        sum += Image.SampleLevel(ClampedSampler, sampleUv, 0).rgb * weight;
        totalWeight += weight;
    }

    return float4(sum / totalWeight, color.a);
}
