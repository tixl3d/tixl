// The faces are rendered with right-handed views, while cube maps are addressed left-handed,
// so each face lands mirrored horizontally.
Texture2DArray<float4> Faces : register(t0);
RWTexture2DArray<float4> CubeMap : register(u0);
SamplerState LinearClamp : register(s0);

[numthreads(16, 16, 1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint width, height, faceCount;
    Faces.GetDimensions(width, height, faceCount);
    if (i.x >= width || i.y >= height)
        return;

    CubeMap[i] = Faces[uint3(width - 1 - i.x, i.y, i.z)];
}

// FXAA 3.11 (quality variant), run per face while copying. The sampler clamps within the array slice, so the
// search stops at a face's border instead of reading its neighbour - edges crossing a cube seam are smoothed
// slightly less there, but nothing bleeds between faces.
static const int SearchSteps = 12;
static const float SearchStepSizes[SearchSteps] = { 1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0, 8.0 };
static const float EdgeThreshold = 0.125;
static const float EdgeThresholdMin = 0.0312;
static const float SubpixelAmount = 0.75;

float Luma(float4 color)
{
    return dot(color.rgb, float3(0.299, 0.587, 0.114));
}

float LumaAt(int2 pixel, int2 maxPixel, uint face)
{
    return Luma(Faces[uint3(clamp(pixel, 0, maxPixel), face)]);
}

float LumaAt(float2 uv, uint face)
{
    return Luma(Faces.SampleLevel(LinearClamp, float3(uv, face), 0));
}

float4 Fxaa(int2 pixel, int2 maxPixel, uint face, float2 texel)
{
    float4 center = Faces[uint3(pixel, face)];
    float lumaM = Luma(center);
    float lumaN = LumaAt(pixel + int2(0, -1), maxPixel, face);
    float lumaS = LumaAt(pixel + int2(0, 1), maxPixel, face);
    float lumaE = LumaAt(pixel + int2(1, 0), maxPixel, face);
    float lumaW = LumaAt(pixel + int2(-1, 0), maxPixel, face);

    float rangeMax = max(max(lumaN, lumaW), max(lumaE, max(lumaS, lumaM)));
    float rangeMin = min(min(lumaN, lumaW), min(lumaE, min(lumaS, lumaM)));
    float range = rangeMax - rangeMin;
    if (range < max(EdgeThresholdMin, rangeMax * EdgeThreshold))
        return center;

    float lumaNW = LumaAt(pixel + int2(-1, -1), maxPixel, face);
    float lumaNE = LumaAt(pixel + int2(1, -1), maxPixel, face);
    float lumaSW = LumaAt(pixel + int2(-1, 1), maxPixel, face);
    float lumaSE = LumaAt(pixel + int2(1, 1), maxPixel, face);

    float lumaNS = lumaN + lumaS;
    float lumaWE = lumaW + lumaE;
    float lumaNESE = lumaNE + lumaSE;
    float lumaNWNE = lumaNW + lumaNE;
    float lumaNWSW = lumaNW + lumaSW;
    float lumaSWSE = lumaSW + lumaSE;

    float edgeHorz = abs(-2 * lumaW + lumaNWSW) + abs(-2 * lumaM + lumaNS) * 2 + abs(-2 * lumaE + lumaNESE);
    float edgeVert = abs(-2 * lumaS + lumaSWSE) + abs(-2 * lumaM + lumaWE) * 2 + abs(-2 * lumaN + lumaNWNE);
    bool horzSpan = edgeHorz >= edgeVert;

    // Low-pass amount for sub-pixel features too small to have a searchable edge.
    float subpixA = (lumaNS + lumaWE) * 2 + lumaNWSW + lumaNESE;
    float subpixC = saturate(abs(subpixA / 12 - lumaM) / range);
    float subpixF = (-2 * subpixC + 3) * subpixC * subpixC;

    if (!horzSpan)
    {
        lumaN = lumaW;
        lumaS = lumaE;
    }

    float lengthSign = horzSpan ? texel.y : texel.x;
    float gradientN = lumaN - lumaM;
    float gradientS = lumaS - lumaM;
    bool pairN = abs(gradientN) >= abs(gradientS);
    float gradientScaled = max(abs(gradientN), abs(gradientS)) * 0.25;
    if (pairN)
        lengthSign = -lengthSign;

    float lumaPairHalf = ((pairN ? lumaN : lumaS) + lumaM) * 0.5;
    bool lumaMLTZero = lumaM - lumaPairHalf < 0;

    // Walk along the edge in both directions until its contrast drops away.
    float2 uv = (pixel + 0.5) * texel;
    float2 posB = uv;
    if (horzSpan)
        posB.y += lengthSign * 0.5;
    else
        posB.x += lengthSign * 0.5;

    float2 offNP = horzSpan ? float2(texel.x, 0) : float2(0, texel.y);
    float2 posN = posB - offNP * SearchStepSizes[0];
    float2 posP = posB + offNP * SearchStepSizes[0];
    float lumaEndN = LumaAt(posN, face) - lumaPairHalf;
    float lumaEndP = LumaAt(posP, face) - lumaPairHalf;
    bool doneN = abs(lumaEndN) >= gradientScaled;
    bool doneP = abs(lumaEndP) >= gradientScaled;

    for (int i = 1; i < SearchSteps && !(doneN && doneP); i++)
    {
        if (!doneN)
        {
            posN -= offNP * SearchStepSizes[i];
            lumaEndN = LumaAt(posN, face) - lumaPairHalf;
            doneN = abs(lumaEndN) >= gradientScaled;
        }

        if (!doneP)
        {
            posP += offNP * SearchStepSizes[i];
            lumaEndP = LumaAt(posP, face) - lumaPairHalf;
            doneP = abs(lumaEndP) >= gradientScaled;
        }
    }

    float dstN = horzSpan ? uv.x - posN.x : uv.y - posN.y;
    float dstP = horzSpan ? posP.x - uv.x : posP.y - uv.y;
    bool directionN = dstN < dstP;
    bool goodSpan = ((directionN ? lumaEndN : lumaEndP) < 0) != lumaMLTZero;
    float pixelOffset = goodSpan ? 0.5 - min(dstN, dstP) / (dstN + dstP) : 0;
    float offset = max(pixelOffset, subpixF * subpixF * SubpixelAmount);

    if (horzSpan)
        uv.y += offset * lengthSign;
    else
        uv.x += offset * lengthSign;

    return Faces.SampleLevel(LinearClamp, float3(uv, face), 0);
}

[numthreads(16, 16, 1)]
void mainFxaa(uint3 i : SV_DispatchThreadID)
{
    uint width, height, faceCount;
    Faces.GetDimensions(width, height, faceCount);
    if (i.x >= width || i.y >= height)
        return;

    int2 source = int2(width - 1 - i.x, i.y);
    CubeMap[i] = Fxaa(source, int2(width - 1, height - 1), i.z, 1.0 / float2(width, height));
}
