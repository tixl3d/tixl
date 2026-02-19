Texture2D<float4> ImageTexture : register(t0);
Texture2D<float4> DepthTexture : register(t1);
Texture2D<float4> NoiseTexture : register(t2);

#include "shared/hash-functions.hlsl"

sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    // float2 DepthRange;
    float Near;
    float Far;
    float2 FarClipRange;

    float4 Color;

    float2 BoostShadows;
    float Passes;
    float KernelSize;

    float MixOriginal;
    float MultiplyOriginal;
    float2 NoiseOffset;

    float AspectRatio;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

float DepthToSceneZ(float depth)
{
    float n = Near;
    float f = Far;
    return (2.0 * n) / (f + n - depth * (f - n)) * (Far - Near) + Near;
}

SamplerState samLinear
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = Wrap;
    AddressV = Wrap;
};

const static int KERNEL_SIZE = 8;
const static float3 avKernel[KERNEL_SIZE] = {
    normalize(float3(1, 1, 1)) * 0.125,
    normalize(float3(-1, -1, -1)) * 0.250,
    normalize(float3(-1, -1, 1)) * 0.375,
    normalize(float3(-1, 1, -1)) * 0.500,
    normalize(float3(-1, 1, 1)) * 0.625,
    normalize(float3(1, -1, -1)) * 0.750,
    normalize(float3(1, -1, 1)) * 0.875,
    normalize(float3(1, 1, -1)) * 1.000};

const static float2 textureSize = float2(256, 256);


float4 blend(float4 tA, float4 tB) 
{
    return float4((1.0 - tB.a) * tA.rgb + tB.a * tB.rgb, 
    tA.a + tB.a - tA.a * tB.a);
}


float4 psMain(vsOutput psInput) : SV_TARGET
{
    // return float4(Passes, 0,0,1);
    // return DepthTexture.Sample(samLinear, psInput.texCoord.xy);
    float refDepth = DepthTexture.SampleLevel(samLinear, psInput.texCoord, 0).r;

    float2 texelSize = float2(1.0 / textureSize.x, 1.0 / textureSize.y);
    float factor = 0;
    float factorIncrement = 1.0 / (Passes * float(KERNEL_SIZE));

    float refSceneZ = DepthToSceneZ(refDepth);
    float2 unstretch =  float2(AspectRatio, 1);

    for (int j = 0; j < Passes; j++)
    {
        // float2 randomCoords = float2(fmod(psInput.texCoord.x * AspectRatio * 21.7 + 2.412 * j + NoiseOffset.x, 1),
        //                              fmod(psInput.texCoord.y * 23.91 + 1.1123 * j + NoiseOffset.y, 1));

        float2 randomCoords = hash22(psInput.texCoord * unstretch * 11.213 + 112.1 * j);

        float3 random = NoiseTexture.SampleLevel(samLinear, randomCoords, 0).rgb;

        random = normalize(random * 2.0 - 1.0);
        for (int i = 0; i < KERNEL_SIZE; i++)
        {
            float3 vRotatedKernel = normalize(reflect(avKernel[i], random)); // * vKernelScale;
            float2 offset = vRotatedKernel.xy * KernelSize * texelSize /unstretch + psInput.texCoord.xy;
            float depth = DepthTexture.SampleLevel(samLinear, offset, 0).r;

            float sceneZ = DepthToSceneZ(depth);
            if (refSceneZ < FarClipRange.y && refSceneZ < sceneZ + 0.001)
            {
                factor += factorIncrement;
            }
        }
    }

    float4 orgColor = ImageTexture.Sample(samLinear, psInput.texCoord);
    factor = pow(saturate(factor + BoostShadows.x), BoostShadows.y);

    float3 AOColor = lerp(Color.rgb, float3(1, 1, 1), factor); // mix shadow color
    float fadeInBackgroundFactor = clamp((refSceneZ - FarClipRange.x) / (FarClipRange.y - FarClipRange.x), 0, 1);

    AOColor = lerp(float3(1, 1, 1), AOColor, Color.a); // fade with color Alpha
    AOColor = lerp(AOColor, float3(1, 1, 1), (fadeInBackgroundFactor));

    float4 blendedColor = MixOriginal < 1 ? orgColor * MixOriginal
                                          : orgColor + (1 - orgColor) * (MixOriginal - 1);
    blendedColor *= MultiplyOriginal;
    return float4(blendedColor.rgb * AOColor, orgColor.a);

    // Experimenting with transparent backgrounds...
    // float4 orgColor = ImageTexture.Sample(samLinear, psInput.texCoord);
    // factor = pow(saturate(factor + BoostShadows.x), BoostShadows.y);
    // float4 AOColor = float4(Color.rgb, Color.a * factor); // mix shadow color
    // float fadeInBackgroundFactor = clamp((refSceneZ - FarClipRange.x) / (FarClipRange.y - FarClipRange.x), 0, 1);
    // AOColor = lerp(AOColor, float4(1, 1, 1, AOColor.a * (1-fadeInBackgroundFactor)), (fadeInBackgroundFactor));
    // return blend(orgColor, AOColor);

    // float4 blendedColor = MixOriginal < 1 ? orgColor * MixOriginal
    //                                       : orgColor + (1 - orgColor) * (MixOriginal - 1);
    // blendedColor *= MultiplyOriginal;
    // return float4(blendedColor.rgb * AOColor, orgColor.a);    
}
