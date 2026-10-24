#include "shared/cubemap-eac.hlsl"

// Progressive refinement of a raymarched cube map. Each frame raymarches one texel per tile into Current.
// resolveMain blends those samples into the history (reprojected when the cube moved), fillMain fills texels
// that have no sample yet from the finest grid the sampling order has completed.

cbuffer Params : register(b0)
{
    float4x4 CubeToWorld;
    float4x4 WorldToPrevCube;

    int Size;
    int TileSize;
    int2 SampleOffset;

    int ResetHistory;
    int IsMoving;
    int EquiAngular;
    float MaxSamples;

    float MaxSamplesWhileMoving;
    float DisocclusionThreshold;
    int LatticeSpacing;
    float NeighborhoodClipping;

    float4 MissColor;
}

// Both passes share these: resolve reads the previous history and writes the new one,
// fill reads the new history and writes the cube map into ResultColor.
Texture2DArray<float4> Current : register(t0);
Texture2DArray<float4> SourceColor : register(t1);
Texture2DArray<float2> SourceData : register(t2);

RWTexture2DArray<float4> ResultColor : register(u0);
RWTexture2DArray<float2> ResultData : register(u1);

// Same table as _RenderCubeMapFaces, in cube map order +X, -X, +Y, -Y, +Z, -Z.
static const float3 FaceRight[6] = { float3(0, 0, -1), float3(0, 0, 1), float3(1, 0, 0), float3(1, 0, 0), float3(1, 0, 0), float3(-1, 0, 0) };
static const float3 FaceDown[6] = { float3(0, -1, 0), float3(0, -1, 0), float3(0, 0, 1), float3(0, 0, -1), float3(0, -1, 0), float3(0, -1, 0) };
static const float3 FaceForward[6] = { float3(1, 0, 0), float3(-1, 0, 0), float3(0, 1, 0), float3(0, -1, 0), float3(0, 0, 1), float3(0, 0, -1) };

float3 TexelToCubeDirection(uint3 texel)
{
    float2 uv = (texel.xy + 0.5) / Size * 2 - 1;
    if (EquiAngular != 0)
        uv = EacToCubeFace(uv);

    return normalize(FaceForward[texel.z] + uv.x * FaceRight[texel.z] + uv.y * FaceDown[texel.z]);
}

// Current holds the hit distance in alpha, a near miss's coverage negated, or 0 where the ray missed.
float4 DecodeSample(float4 sample, out float hitDistance)
{
    bool isHit = sample.a > 0;
    hitDistance = isHit ? sample.a : -1;
    float coverage = isHit ? 1 : saturate(-sample.a);
    return float4(lerp(MissColor.rgb, sample.rgb, coverage), lerp(MissColor.a, 1, coverage));
}

// Unsampled texels have no hit distance of their own this frame, so reprojection takes it from the coarse samples:
// the texel's own tile if that hit, otherwise the closest neighbor tile, so edges reproject with the foreground.
float GetCurrentHitDistance(uint3 texel)
{
    int2 tile = (int2)texel.xy / TileSize;
    float ownDistance = Current[uint3(tile, texel.z)].a;
    if (ownDistance > 0)
        return ownDistance;

    int lastTile = Size / TileSize - 1;
    float closest = -1;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            int2 neighbor = clamp(tile + int2(x, y), 0, lastTile);
            float distance = Current[uint3(neighbor, texel.z)].a;
            if (distance > 0 && (closest < 0 || distance < closest))
                closest = distance;
        }
    }

    return closest;
}

// Position on a face in texels, continuous, so it can be filtered.
float2 CubeDirectionToFacePosition(float3 direction, out uint face)
{
    float3 a = abs(direction);
    face = a.x >= a.y && a.x >= a.z ? (direction.x > 0 ? 0 : 1)
         : a.y >= a.z               ? (direction.y > 0 ? 2 : 3)
                                    : (direction.z > 0 ? 4 : 5);

    float major = dot(direction, FaceForward[face]);
    float2 uv = float2(dot(direction, FaceRight[face]), dot(direction, FaceDown[face])) / major;
    if (EquiAngular != 0)
        uv = CubeFaceToEac(uv);

    return (uv * 0.5 + 0.5) * Size;
}

uint3 CubeDirectionToTexel(float3 direction)
{
    uint face;
    float2 position = CubeDirectionToFacePosition(direction, face);
    return uint3((uint2)clamp(position, 0, Size - 1), face);
}

// Reprojecting every frame with the nearest texel leaves edges ragged, and bilinear filtering blurs them a little more
// each frame; Catmull-Rom keeps them sharp. The result is limited to the four nearest texels so it cannot ring.
float4 SampleHistoryCatmullRom(float3 direction)
{
    uint face;
    float2 position = CubeDirectionToFacePosition(direction, face) - 0.5;
    int2 base = (int2)floor(position);
    float2 t = position - base;

    float2 w0 = t * (-0.5 + t * (1 - 0.5 * t));
    float2 w1 = 1 + t * t * (-2.5 + 1.5 * t);
    float2 w2 = t * (0.5 + t * (2 - 1.5 * t));
    float2 w3 = t * t * (-0.5 + 0.5 * t);
    float weightsX[4] = { w0.x, w1.x, w2.x, w3.x };
    float weightsY[4] = { w0.y, w1.y, w2.y, w3.y };

    float4 sum = 0;
    float4 nearestMin = 1e30;
    float4 nearestMax = -1e30;
    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {
            float4 color = SourceColor[uint3(clamp(base + int2(x - 1, y - 1), 0, Size - 1), face)];
            sum += color * (weightsX[x] * weightsY[y]);
            if (x == 1 || x == 2)
            {
                if (y == 1 || y == 2)
                {
                    nearestMin = min(nearestMin, color);
                    nearestMax = max(nearestMax, color);
                }
            }
        }
    }

    return clamp(sum, nearestMin, nearestMax);
}

float3 RgbToYCoCg(float3 c)
{
    return float3(dot(c, float3(0.25, 0.5, 0.25)), dot(c, float3(0.5, 0, -0.5)), dot(c, float3(-0.25, 0.5, -0.25)));
}

float3 YCoCgToRgb(float3 c)
{
    return float3(c.x + c.y - c.z, c.x + c.z, c.x - c.y - c.z);
}

// Pulls the history toward this frame's coarse samples around the texel, so changes and ghosts fade at once instead
// of over many frames. The box is the samples' mean ± NeighborhoodClipping standard deviations.
float4 ClipToNeighborhood(float4 history, uint3 texel)
{
    int2 tile = (int2)texel.xy / TileSize;
    int lastTile = Size / TileSize - 1;
    float3 sum = 0;
    float3 sumOfSquares = 0;
    float alphaMin = 1e30;
    float alphaMax = -1e30;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float unusedDistance;
            float4 neighbor = DecodeSample(Current[uint3(clamp(tile + int2(x, y), 0, lastTile), texel.z)], unusedDistance);
            float3 neighborYCoCg = RgbToYCoCg(neighbor.rgb);
            sum += neighborYCoCg;
            sumOfSquares += neighborYCoCg * neighborYCoCg;
            alphaMin = min(alphaMin, neighbor.a);
            alphaMax = max(alphaMax, neighbor.a);
        }
    }

    float3 mean = sum / 9;
    float3 deviation = sqrt(max(sumOfSquares / 9 - mean * mean, 0));
    float3 extent = deviation * NeighborhoodClipping + 0.0001;

    float3 offset = RgbToYCoCg(history.rgb) - mean;
    float3 offsetInExtents = abs(offset / extent);
    float largestOffset = max(offsetInExtents.x, max(offsetInExtents.y, offsetInExtents.z));
    float3 clipped = largestOffset > 1 ? mean + offset / largestOffset : mean + offset;

    return float4(YCoCgToRgb(clipped), clamp(history.a, alphaMin, alphaMax));
}

[numthreads(8, 8, 1)]
void resolveMain(uint3 texel : SV_DispatchThreadID)
{
    if (texel.x >= (uint)Size || texel.y >= (uint)Size)
        return;

    float4 color = 0;
    float2 data = float2(-1, 0); // hit distance (negative for a miss), sample count

    if (ResetHistory == 0)
    {
        uint3 source = texel;
        float3 historyDirection = 0;
        if (IsMoving != 0)
        {
            float3 direction = mul(TexelToCubeDirection(texel), (float3x3)CubeToWorld);
            float3 origin = CubeToWorld[3].xyz;

            float hitDistance = GetCurrentHitDistance(texel);
            if (hitDistance > 0)
            {
                // Look up where the surface point was seen from the previous center, and keep it only if the
                // history saw the same surface there.
                float3 previousPoint = mul(float4(origin + direction * hitDistance, 1), WorldToPrevCube).xyz;
                historyDirection = previousPoint;
                source = CubeDirectionToTexel(previousPoint);
                float expectedDistance = length(previousPoint);
                float storedDistance = SourceData[source].x;
                bool isDisoccluded = storedDistance <= 0
                                     || abs(storedDistance - expectedDistance) > DisocclusionThreshold * expectedDistance;
                data = isDisoccluded ? float2(-1, 0) : SourceData[source];
            }
            else
            {
                // Nothing near was hit: the background is infinitely far away, so only the rotation moves it. History
                // that saw geometry there belongs to something that has moved away.
                historyDirection = mul(direction, (float3x3)WorldToPrevCube);
                source = CubeDirectionToTexel(historyDirection);
                data = SourceData[source];
                if (data.x > 0)
                    data = float2(-1, 0);
            }

            data.y = min(data.y, MaxSamplesWhileMoving);
        }
        else
        {
            data = SourceData[source];
        }

        color = IsMoving != 0 && data.y > 0 ? SampleHistoryCatmullRom(historyDirection) : SourceColor[source];

        if (NeighborhoodClipping > 0 && data.y > 0)
            color = ClipToNeighborhood(color, texel);
    }

    bool isSampled = all(texel.xy % (uint)TileSize == (uint2)SampleOffset);
    if (isSampled)
    {
        float hitDistance;
        float4 sampleColor = DecodeSample(Current[uint3(texel.xy / (uint)TileSize, texel.z)], hitDistance);
        float sampleCount = min(data.y + 1, MaxSamples);
        color = data.y > 0 ? lerp(color, sampleColor, 1 / sampleCount) : sampleColor;
        data = float2(hitDistance, sampleCount);
    }

    ResultColor[texel] = color;
    ResultData[texel] = data;
}

[numthreads(8, 8, 1)]
void fillMain(uint3 texel : SV_DispatchThreadID)
{
    if (texel.x >= (uint)Size || texel.y >= (uint)Size)
        return;

    if (SourceData[texel].y > 0)
    {
        ResultColor[texel] = SourceColor[texel];
        return;
    }

    // Blend the four surrounding texels of the finest grid every tile has completed.
    int spacing = LatticeSpacing;
    int2 baseTexel = (int2)texel.xy / spacing * spacing;
    float2 f = ((int2)texel.xy - baseTexel) / (float)spacing;
    float4 sum = 0;
    float weightSum = 0;
    for (int corner = 0; corner < 4; corner++)
    {
        int2 offset = int2(corner & 1, corner >> 1);
        int2 neighbor = min(baseTexel + offset * spacing, Size - 1);
        uint3 neighborTexel = uint3(neighbor, texel.z);
        if (SourceData[neighborTexel].y <= 0)
            continue;

        float2 w2 = lerp(1 - f, f, (float2)offset);
        float w = w2.x * w2.y + 1e-4;
        sum += SourceColor[neighborTexel] * w;
        weightSum += w;
    }

    float unusedDistance;
    ResultColor[texel] = weightSum > 0
                         ? sum / weightSum
                         : DecodeSample(Current[uint3(texel.xy / (uint)TileSize, texel.z)], unusedDistance);
}
