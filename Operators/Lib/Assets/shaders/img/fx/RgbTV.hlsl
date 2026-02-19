#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float Visibility;

    float PatternAmount;
    float ImageBrightness;
    float BlackLevel;
    float Contrast;

    float BlurImage;
    float GlowIntensity;
    float GlowBlur;
    float PatternSize;

    float ShiftColums;
    float Gaps;
    float PatternBlurX;
    float PatternBlurY;

    float GlitchAmount;
    float GlitchTime;
    float GlitchDistort;
    float ShadeDistortion;

    float NoiseForDistortion;
    float Noise;
    float NoiseSpeed;
    float NoiseExponent;

    float NoiseColorize;
    float Buldge;
    float Vignette;
}

Texture2D<float4> inputTexture : register(t0);
Texture2D<float4> noiseTexture : register(t1);
sampler clampingSampler : register(s0);
sampler wrappingSampler : register(s1);
// sampler clampingSampler : register(s0);

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

#define mod(x, y) ((x) - ((y)*floor((x) / (y))))

static const float3 RgbColors[] = {
    float3(1, 0, 0),
    float3(0, 1, 0),
    float3(0, 0, 1),
};

float GetColor(int rgbIndex, float x, float py, float sourceImageChannel)
{
    float offset = (rgbIndex - 1) / 3.0;
    x += offset;
    x = mod(x, 1.03);
    // return x;
    float center = (0.5 - abs(x - 0.5)) * 2;

    // return center;
    //  float xx = center ; // pow((center + Contrast + sourceImageChannel * ImageBrightness), BlackLevel);
    float xx = center + pow(sourceImageChannel * ImageBrightness, Contrast) + BlackLevel;
    float s = smoothstep(1 - PatternBlurX, 1 + PatternBlurY, xx) * ImageBrightness;
    return s;
}

float4 GetNoiseFromRandom(float2 uv)
{
    // Animation
    float pxHash = hash12(uv * 431 + 111);
    float t = GlitchTime * NoiseSpeed + pxHash;

    // Color Noise
    float4 hash1 = hash42((uv * 431 + (int)t));
    float4 hash2 = hash42((uv * 431 + (int)t + 1));
    float4 hash = lerp(hash1, hash2, t % 1);

    float4 grayScale = (hash.r + hash.g + hash.b) / 3;
    float4 noise = (lerp(grayScale, hash, NoiseColorize) - 0.5) * 2;

    noise = noise < 0
                ? -pow(-noise, NoiseExponent)
                : pow(noise, NoiseExponent);

    // noise += Brightness;
    return noise;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float aspectRatio = TargetWidth / TargetHeight;

    float2 p = uv;
    p -= 0.5;

    // Bulge distort
    p -= p * Buldge * Visibility * (0.5 - dot(p, p));

    float2 uv2 = p + 0.5;

    // Distortion
    float2 noiseOffset = float2(.1, 0.1) * GlitchTime;
    float2 noiseUv = p * float2(0.001, 1) + noiseOffset;
    float4 noiseColor = abs(noiseTexture.SampleLevel(wrappingSampler, noiseUv, 0) - 0.5) * GlitchAmount * Visibility;

    // Amplify noise on upper edge
    noiseColor *= (0.4 + pow(1 - uv2.y, 6) * 3);
    noiseColor += 0.03;

    float2 glichOffset = pow(noiseColor.r, 4) * GlitchDistort * float2(1, 0.1);
    uv2 -= glichOffset * Visibility;

    int sourceWidth, sourceHeight, mipLevelCount;
    inputTexture.GetDimensions(0, sourceWidth, sourceHeight, mipLevelCount);
    mipLevelCount = 7;

    float4 blurredCol = 0;
    float blurredSum = 0;

    float4 glowCol = 0;
    float glowSum = 0;
    for (int i = 0; i < mipLevelCount + 1; i++)
    {
        float4 mipColor = inputTexture.SampleLevel(clampingSampler, uv2, i);

        // Accumulate blur color
        float f = i / ((float)mipLevelCount);
        float level = saturate((pow(f + 1 - saturate(BlurImage), 20) + 0.001));
        blurredSum += level;
        blurredCol += mipColor * level;

        // Accumulate glow color

        level = saturate((pow(f + 1 - saturate(GlowBlur), 20) + 0.001));
        glowSum += level;
        glowCol += mipColor * level;
    }
    blurredCol /= blurredSum;
    float4 imgCol1 = inputTexture.SampleLevel(clampingSampler, uv2, 0);
    blurredCol = lerp(imgCol1, blurredCol, saturate(BlurImage + 1) * Visibility);

    glowCol /= glowSum;
    float4 imgCol = blurredCol + clamp((glowCol)*GlowIntensity, 0, 10) * Visibility;

    imgCol.rgb *= clamp(1 - pow(noiseColor.r, 1.4) * ShadeDistortion * GlitchDistort * GlitchAmount * Visibility, 0, 10);

    imgCol.a = saturate(imgCol.a);

    float2 divisions = float2(aspectRatio, 1) * 4 / PatternSize;
    // float2 pCentered = (p + PatternOffset / divisions * float2(-1, 1));
    float2 pCentered = p;
    float2 pScaled = pCentered * divisions;

    float pInCellX = mod(pScaled.x, 1);
    int cellIdX = pScaled.x - pInCellX;
    float pInCellY = mod(pScaled.y + cellIdX * ShiftColums, 1);
    int cellIdY = pScaled.y - pInCellY;

    float2 pInCell = float2(pInCellX, pInCellY);

    float4 noise = GetNoiseFromRandom(float2(cellIdX, cellIdY));
    float4 noiseDelta = abs(noise) * ((pow(noiseColor.r, 2)) * GlitchDistort * NoiseForDistortion + 1) * Noise * GlitchAmount;

    int rgbStripeIndex = int(pInCellX * 3);

    float xInStripe = (pInCellX - 0.5) / (1 - Gaps * 2) + 0.5;

    float3 noisyImage = imgCol.rgb + noiseDelta.rgb;
    float3 cc = float3(
        GetColor(0, xInStripe, pInCellY, noisyImage.r),
        GetColor(1, xInStripe, pInCellY, noisyImage.g),
        GetColor(2, xInStripe, pInCellY, noisyImage.b));

    float padding = Gaps;
    float yBlur = smoothstep(padding - PatternBlurY, padding + PatternBlurY, abs(pInCellY - 0.5));

    float4 pattern = float4(cc * yBlur, 1);

    float4 r = lerp(imgCol, pattern, PatternAmount * Visibility);

    // Vignette
    p.x *= 1.5;
    r.rgb *= lerp(1.5 - Vignette * (1 - (0.5 - dot(p, p)) - 0.5), 1, Visibility + 1);

    return float4(r);
}
