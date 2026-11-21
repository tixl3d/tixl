// Raymarches up to three NanoVDB float grids inside an axis-aligned glass box:
//   t0 = liquid level set (surface shaded), t1 = smoke (density from the depth below the zero level),
//   t2 = fire (temperature from the depth below the zero level, shown as emission).
// The grids are read straight from the buffer produced by [VolumeToNanoVdb] (see NanoVdbBuffer.cs for the layout).

cbuffer Params : register(b0)
{
    float3 BoxMin;
    float Time;

    float3 BoxMax;
    float StepCount;

    float3 LightDirection;
    float LiquidAmount;

    float4 LiquidColor;

    float3 SmokeColor;
    float SmokeDensity;

    float FireIntensity;
    float SmokeSoftness;
    float FireSoftness;
    float NoiseAmount;

    float4 EdgeColor;
}

cbuffer Transforms : register(b1)
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

StructuredBuffer<uint> LiquidGrid : register(t0);
StructuredBuffer<uint> SmokeGrid : register(t1);
StructuredBuffer<uint> FireGrid : register(t2);

struct vsOutput
{
    float4 position : SV_POSITION;
    float3 worldTViewDir : TEXCOORD1;
    float3 worldTViewPos : TEXCOORD2;
};

static const float3 Quad[] =
    {
        float3(-1, -1, 0),
        float3(1, -1, 0),
        float3(1, 1, 0),
        float3(1, 1, 0),
        float3(-1, 1, 0),
        float3(-1, -1, 0),
};

vsOutput vsMain(uint vertexId : SV_VertexID)
{
    vsOutput output;
    float4 quadPos = float4(Quad[vertexId], 1);
    float2 texCoord = quadPos.xy * float2(0.5, -0.5) + 0.5;
    output.position = quadPos;

    float4 viewTNearFragPos = float4(texCoord.x * 2.0 - 1.0, -texCoord.y * 2.0 + 1.0, 0.0, 1.0);
    float4 worldTNearFragPos = mul(viewTNearFragPos, ClipSpaceToWorld);
    worldTNearFragPos /= worldTNearFragPos.w;

    float4 viewTFarFragPos = float4(texCoord.x * 2.0 - 1.0, -texCoord.y * 2.0 + 1.0, 1.0, 1.0);
    float4 worldTFarFragPos = mul(viewTFarFragPos, ClipSpaceToWorld);
    worldTFarFragPos /= worldTFarFragPos.w;

    output.worldTViewDir = normalize(worldTFarFragPos.xyz - worldTNearFragPos.xyz);
    output.worldTViewPos = worldTNearFragPos.xyz;
    return output;
}

//--- NanoVDB float grid accessor ------------------------------------------------------------

static const uint GridDataBytes = 672;
static const uint MapMatfBytes = 296;
static const uint MapVecfBytes = 296 + 72;
static const uint RootHeaderBytes = 64;
static const uint RootTileBytes = 32;
static const uint UpperChildMaskBytes = 24 + 8 + 4096;
static const uint UpperTableBytes = 8256;
static const uint LowerChildMaskBytes = 24 + 8 + 512;
static const uint LowerTableBytes = 1088;
static const uint LeafValuesBytes = 96;

struct GridInfo
{
    uint rootOffset;
    uint tileCount;
    float background;
    float voxelSize;
    float3 translation;
    int3 indexMin;
    int3 indexMax;
    float3 worldMin;
    float3 worldMax;
};

GridInfo ReadGridInfo(StructuredBuffer<uint> g)
{
    GridInfo info;
    info.rootOffset = GridDataBytes + g[(GridDataBytes + 24) >> 2];
    uint root = info.rootOffset >> 2;
    info.indexMin = int3(g[root], g[root + 1], g[root + 2]);
    info.indexMax = int3(g[root + 3], g[root + 4], g[root + 5]);
    info.tileCount = g[root + 6];
    info.background = asfloat(g[root + 7]);
    info.voxelSize = asfloat(g[MapMatfBytes >> 2]);
    uint vec = MapVecfBytes >> 2;
    info.translation = float3(asfloat(g[vec]), asfloat(g[vec + 1]), asfloat(g[vec + 2]));

    // Outside this box NanoSample returns the background, so rays can skip it.
    info.worldMin = info.translation + (info.indexMin - 1) * info.voxelSize;
    info.worldMax = info.translation + (info.indexMax + 1) * info.voxelSize;
    return info;
}

// Walks root tile -> upper (32^3) -> lower (16^3). Returns the leaf byte offset, or 0 when the voxel lies in a constant tile whose value goes to tileValue.
uint NanoFindLeaf(StructuredBuffer<uint> g, GridInfo info, int3 ijk, out float tileValue)
{
    tileValue = info.background;

    uint3 key = uint3(ijk >> 12) & 0x1FFFFFu;
    uint keyLow = key.z | ((key.y & 0x7FFu) << 21);
    uint keyHigh = (key.y >> 11) | (key.x << 10);

    uint upper = 0;
    uint firstTile = (info.rootOffset + RootHeaderBytes) >> 2;
    [loop]
    for (uint tileIndex = 0; tileIndex < info.tileCount; tileIndex++)
    {
        uint tile = firstTile + tileIndex * (RootTileBytes >> 2);
        if (g[tile] != keyLow || g[tile + 1] != keyHigh)
            continue;

        uint child = g[tile + 2];
        if (child == 0)
        {
            tileValue = asfloat(g[tile + 5]);
            return 0;
        }

        upper = info.rootOffset + child;
        break;
    }

    if (upper == 0)
        return 0;

    uint3 u = uint3(ijk & 4095) >> 7;
    uint upperSlot = (u.x << 10) | (u.y << 5) | u.z;
    uint upperWord = g[(upper + UpperChildMaskBytes) / 4 + (upperSlot >> 5)];
    uint upperEntry = (upper + UpperTableBytes) / 4 + upperSlot * 2;
    if ((upperWord & (1u << (upperSlot & 31))) == 0)
    {
        tileValue = asfloat(g[upperEntry]);
        return 0;
    }

    uint lower = upper + g[upperEntry];
    uint3 l = uint3(ijk & 127) >> 3;
    uint lowerSlot = (l.x << 8) | (l.y << 4) | l.z;
    uint lowerWord = g[(lower + LowerChildMaskBytes) / 4 + (lowerSlot >> 5)];
    uint lowerEntry = (lower + LowerTableBytes) / 4 + lowerSlot * 2;
    if ((lowerWord & (1u << (lowerSlot & 31))) == 0)
    {
        tileValue = asfloat(g[lowerEntry]);
        return 0;
    }

    return lower + g[lowerEntry];
}

uint NanoLeafValueIndex(uint leaf, int3 ijk)
{
    uint3 v = uint3(ijk & 7);
    return (leaf + LeafValuesBytes) / 4 + ((v.x << 6) | (v.y << 3) | v.z);
}

float NanoLookup(StructuredBuffer<uint> g, GridInfo info, int3 ijk)
{
    float tileValue;
    uint leaf = NanoFindLeaf(g, info, ijk, tileValue);
    return leaf == 0 ? tileValue : asfloat(g[NanoLeafValueIndex(leaf, ijk)]);
}

// The last leaf a ray touched: neighbouring samples almost always fall into the same 8^3 block, which skips the tree walk.
struct LeafCache
{
    int3 block;
    uint leaf;
    float tileValue;
};

LeafCache NewLeafCache()
{
    LeafCache cache;
    cache.block = int3(0x7FFFFFFF, 0, 0);
    cache.leaf = 0;
    cache.tileValue = 0;
    return cache;
}

// Trilinear sample between voxel centers: voxel i is sampled at translation + i * size.
float NanoSample(StructuredBuffer<uint> g, GridInfo info, float3 worldPos, inout LeafCache cache)
{
    float3 q = (worldPos - info.translation) / info.voxelSize;
    int3 i0 = (int3)floor(q);
    if (any(i0 < info.indexMin - 1) || any(i0 > info.indexMax))
        return info.background;

    float3 f = q - i0;

    // Most samples have all eight corners inside one leaf (or one constant tile): walk the tree at most once.
    if (all((i0 & 7) < 7))
    {
        int3 block = i0 & ~7;
        if (any(block != cache.block))
        {
            cache.leaf = NanoFindLeaf(g, info, i0, cache.tileValue);
            cache.block = block;
        }

        if (cache.leaf == 0)
            return cache.tileValue;

        // Leaf offsets are x * 64 + y * 8 + z.
        uint corner = NanoLeafValueIndex(cache.leaf, i0);
        float c000 = asfloat(g[corner]);
        float c001 = asfloat(g[corner + 1]);
        float c010 = asfloat(g[corner + 8]);
        float c011 = asfloat(g[corner + 9]);
        float c100 = asfloat(g[corner + 64]);
        float c101 = asfloat(g[corner + 65]);
        float c110 = asfloat(g[corner + 72]);
        float c111 = asfloat(g[corner + 73]);
        return lerp(lerp(lerp(c000, c100, f.x), lerp(c010, c110, f.x), f.y),
                    lerp(lerp(c001, c101, f.x), lerp(c011, c111, f.x), f.y), f.z);
    }

    // The cell straddles leaves: accumulate the weighted corners (no local arrays, they end up in slow local memory).
    float sum = 0;
    [loop]
    for (uint k = 0; k < 8; k++)
    {
        int3 offset = int3(k >> 2, (k >> 1) & 1, k & 1);
        float3 axisWeight = lerp(1.0 - f, f, (float3)offset);
        sum += axisWeight.x * axisWeight.y * axisWeight.z * NanoLookup(g, info, i0 + offset);
    }

    return sum;
}

float3 NanoGradient(StructuredBuffer<uint> g, GridInfo info, float3 p)
{
    float e = info.voxelSize;
    float3 gradient = 0;
    LeafCache cache = NewLeafCache();
    [loop]
    for (int axis = 0; axis < 3; axis++)
    {
        float3 direction = float3(axis == 0 ? 1.0 : 0.0, axis == 1 ? 1.0 : 0.0, axis == 2 ? 1.0 : 0.0);
        gradient += direction * (NanoSample(g, info, p + direction * e, cache) - NanoSample(g, info, p - direction * e, cache));
    }

    return gradient;
}

//--- Procedural detail ------------------------------------------------------------------------

float Hash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float ValueNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                     lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                     lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float Fbm(float3 p)
{
    float v = 0.5 * ValueNoise(p);
    v += 0.25 * ValueNoise(p * 2.03 + 11.7);
    v += 0.125 * ValueNoise(p * 4.01 + 3.1);
    v += 0.0625 * ValueNoise(p * 8.07 + 7.3);
    return v / 0.9375;
}

float3 FireRamp(float t)
{
    float3 c = float3(saturate(t * 3.0), saturate(t * 3.0 - 1.0), saturate(t * 3.0 - 2.0));
    return pow(c, 1.4);
}

float3 SkyColor(float3 dir)
{
    float3 sky = lerp(float3(0.12, 0.16, 0.24), float3(0.65, 0.78, 0.95), saturate(dir.y * 0.7 + 0.3));
    // Bright window-like patch in the direction of the light.
    float lightPatch = pow(saturate(dot(dir, normalize(LightDirection))), 24.0);
    return sky + lightPatch * 1.5;
}

bool IntersectBounds(float3 boundsMin, float3 boundsMax, float3 ro, float3 rd, out float tNear, out float tFar)
{
    float3 inv = 1.0 / (abs(rd) < 1e-6 ? 1e-6 : rd);
    float3 t0 = (boundsMin - ro) * inv;
    float3 t1 = (boundsMax - ro) * inv;
    float3 tMin = min(t0, t1);
    float3 tMax = max(t0, t1);
    tNear = max(max(tMin.x, tMin.y), max(tMin.z, 0.0));
    tFar = min(min(tMax.x, tMax.y), tMax.z);
    return tFar > tNear;
}

bool IntersectBox(float3 ro, float3 rd, out float tNear, out float tFar)
{
    return IntersectBounds(BoxMin, BoxMax, ro, rd, tNear, tFar);
}

// 0..1 coverage of the box wireframe at a point on the box surface.
float EdgeCoverage(float3 p, float thickness)
{
    float3 nearFace = min(abs(p - BoxMin), abs(p - BoxMax));
    float3 lineCoverage = 1.0 - smoothstep(thickness * 0.5, thickness, nearFace);
    float edge = lineCoverage.x * lineCoverage.y + lineCoverage.y * lineCoverage.z + lineCoverage.x * lineCoverage.z;
    return saturate(edge);
}

float SmokeDensityAt(float3 p, float sdf, bool withNoise)
{
    // Noise erodes the boundary instead of just dimming the interior, which gives wispy edges.
    float reach = SmokeSoftness * 2.2 * NoiseAmount;
    if (-sdf + 0.55 * reach <= 0)
        return 0;

    float detail = withNoise ? Fbm(p * 3.0 + float3(0, -Time * 0.5, Time * 0.1)) : 0.45;
    float eroded = -sdf + (detail - 0.45) * reach;
    return smoothstep(0.0, max(SmokeSoftness, 1e-4), eroded);
}

float FireTemperatureAt(float3 p, float sdf, GridInfo info)
{
    float reach = FireSoftness * 1.6 * NoiseAmount;
    if (-sdf + 0.55 * reach <= 0)
        return 0;

    float flicker = Fbm(p * 4.5 + float3(0, -Time * 2.4, Time * 0.3));
    float eroded = -sdf + (flicker - 0.45) * reach;
    float heat = smoothstep(0.0, max(FireSoftness, 1e-4), eroded);

    // Flames cool towards the top of their own grid.
    float bottom = info.translation.y + info.indexMin.y * info.voxelSize;
    float top = info.translation.y + (info.indexMax.y + 1) * info.voxelSize;
    float height = saturate((p.y - bottom) / max(top - bottom, 1e-4));
    return heat * (1.0 - 0.75 * smoothstep(0.2, 1.0, height));
}

float4 psMain(vsOutput input) : SV_TARGET
{
    float3 rayOrigin = input.worldTViewPos;
    float3 rayDir = normalize(input.worldTViewDir);

    float tNear, tFar;
    if (!IntersectBox(rayOrigin, rayDir, tNear, tFar))
        return 0;

    GridInfo liquidInfo = ReadGridInfo(LiquidGrid);
    GridInfo smokeInfo = ReadGridInfo(SmokeGrid);
    GridInfo fireInfo = ReadGridInfo(FireGrid);

    float3 lightDir = normalize(LightDirection);
    float3 boxSize = BoxMax - BoxMin;
    float steps = max(StepCount, 8.0);
    float stepLength = (tFar - tNear) / steps;

    // Only march where an enabled grid can contribute.
    float marchStart = tFar;
    float marchEnd = tNear;
    float a, b;
    if (LiquidAmount > 0 && IntersectBounds(max(liquidInfo.worldMin, BoxMin), min(liquidInfo.worldMax, BoxMax), rayOrigin, rayDir, a, b))
    {
        marchStart = min(marchStart, a);
        marchEnd = max(marchEnd, b);
    }

    if (SmokeDensity > 0 && IntersectBounds(max(smokeInfo.worldMin, BoxMin), min(smokeInfo.worldMax, BoxMax), rayOrigin, rayDir, a, b))
    {
        marchStart = min(marchStart, a);
        marchEnd = max(marchEnd, b);
    }

    if (FireIntensity > 0 && IntersectBounds(max(fireInfo.worldMin, BoxMin), min(fireInfo.worldMax, BoxMax), rayOrigin, rayDir, a, b))
    {
        marchStart = min(marchStart, a);
        marchEnd = max(marchEnd, b);
    }

    float stepCount = marchEnd > marchStart ? ceil((marchEnd - marchStart) / stepLength) : 0;

    float3 color = 0;
    float transmittance = 1;
    float previousLiquid = 1e5;
    float previousT = marchStart;
    LeafCache liquidCache = NewLeafCache();
    LeafCache smokeCache = NewLeafCache();
    LeafCache fireCache = NewLeafCache();

    [loop]
    for (float i = 0; i < stepCount && transmittance > 0.02; i++)
    {
        float t = marchStart + (i + 0.5) * stepLength;
        float3 p = rayOrigin + rayDir * t;

        if (LiquidAmount > 0)
        {
            float liquid = NanoSample(LiquidGrid, liquidInfo, p, liquidCache);
            if (liquid <= 0 && previousLiquid > 0)
            {
                // Refine the zero crossing between the last two samples.
                float lo = previousT;
                float hi = t;
                LeafCache bisectCache = NewLeafCache();
                [loop]
                for (int b = 0; b < 5; b++)
                {
                    float mid = 0.5 * (lo + hi);
                    if (NanoSample(LiquidGrid, liquidInfo, rayOrigin + rayDir * mid, bisectCache) > 0)
                        lo = mid;
                    else
                        hi = mid;
                }

                float3 hitPos = rayOrigin + rayDir * hi;
                float3 normal = normalize(NanoGradient(LiquidGrid, liquidInfo, hitPos) + 1e-6);
                float3 ripple = float3(ValueNoise(hitPos * 5.0 + Time * 0.5), ValueNoise(hitPos * 5.0 + 31.4 - Time * 0.4), ValueNoise(hitPos * 5.0 + 17.9)) - 0.5;
                normal = normalize(normal + ripple * 0.4 * NoiseAmount * saturate(normal.y + 0.2));
                float3 view = -rayDir;
                float fresnel = 0.04 + 0.9 * pow(1.0 - saturate(dot(normal, view)), 4.0);
                float diffuse = 0.35 + 0.65 * saturate(dot(normal, lightDir));
                float3 reflection = SkyColor(reflect(rayDir, normal));
                // The flame lights the surface around its base.
                float3 fireDistance = hitPos - float3(0, hitPos.y, 0);
                reflection += float3(1.0, 0.45, 0.12) * FireIntensity * 0.12 / (1.0 + 6.0 * dot(fireDistance, fireDistance));
                float specular = pow(saturate(dot(normal, normalize(lightDir + view))), 140.0) * 1.6;
                float depthTint = saturate(0.85 + 0.5 * (hitPos.y - BoxMin.y) / boxSize.y);
                float3 surface = lerp(LiquidColor.rgb * diffuse * depthTint, reflection, fresnel) + specular;

                float alpha = saturate(LiquidAmount * LiquidColor.a);
                color += transmittance * alpha * surface;
                // Whatever the water lets through is the dim body of the liquid.
                color += transmittance * (1 - alpha) * LiquidColor.rgb * 0.12;
                transmittance *= (1 - alpha) * 0.4;
                break;
            }

            previousLiquid = liquid;
            previousT = t;
        }

        if (SmokeDensity > 0)
        {
            float sdf = NanoSample(SmokeGrid, smokeInfo, p, smokeCache);
            float density = SmokeDensityAt(p, sdf, true);
            if (density > 0.001)
            {
                // Self shadowing: a few taps towards the light through the same grid.
                float shadowOpacity = 0;
                LeafCache shadowCache = NewLeafCache();
                [loop]
                for (int s = 1; s <= 2; s++)
                {
                    float3 sp = p + lightDir * (0.22 * s);
                    shadowOpacity += SmokeDensityAt(sp, NanoSample(SmokeGrid, smokeInfo, sp, shadowCache), false);
                }

                float lightAmount = exp(-shadowOpacity * SmokeDensity * 0.24);
                float heightFactor = saturate((p.y - BoxMin.y) / boxSize.y);
                float3 warmGlow = float3(1.0, 0.45, 0.12) * FireIntensity * 0.15 * (1.0 - heightFactor);
                float3 lit = SmokeColor * (0.25 + 1.1 * lightAmount) + warmGlow * SmokeColor;

                float absorbed = 1.0 - exp(-density * SmokeDensity * stepLength * 3.0);
                color += transmittance * absorbed * lit;
                transmittance *= (1.0 - absorbed);
            }
        }

        if (FireIntensity > 0)
        {
            float sdf = NanoSample(FireGrid, fireInfo, p, fireCache);
            float temperature = FireTemperatureAt(p, sdf, fireInfo);
            if (temperature > 0.001)
            {
                color += transmittance * FireRamp(temperature) * temperature * FireIntensity * stepLength * 9.0;
                transmittance *= exp(-temperature * stepLength * 3.0);
            }
        }
    }

    // Glass edges: front and back hit points together cover all twelve box edges.
    float edgeThickness = 0.006 + 0.004 * tNear;
    float edge = max(EdgeCoverage(rayOrigin + rayDir * tNear, edgeThickness),
                     EdgeCoverage(rayOrigin + rayDir * tFar, edgeThickness) * 0.6) * EdgeColor.a;
    float alpha = 1.0 - transmittance;
    color = color * (1.0 - edge) + EdgeColor.rgb * edge;
    alpha = alpha * (1.0 - edge) + edge;

    // A faint tint marks the glass faces.
    color += 0.015 * transmittance * (1.0 - edge);
    alpha = saturate(alpha + 0.015 * transmittance);

    return float4(color, alpha);
}
