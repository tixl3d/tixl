#include "shared/pbr.hlsl"


#ifndef __AdjustRoughnessForSpecularAA
#define __AdjustRoughnessForSpecularAA

inline float AdjustRoughnessForSpecularAA(float baseRoughness, float specularAA)
{
 // --- Specular anti-aliasing ---
    // Compute normal variance using screen-space derivatives and increase roughness accordingly.
    // This reduces specular aliasing on silhouettes and high-frequency normalmap regions.
    float3 Nx = ddx(frag.N);
    float3 Ny = ddy(frag.N);
    float normalVar = max(0.0, max(dot(Nx, Nx), dot(Ny, Ny)));
    normalVar *= specularAA;
    // convert roughness -> alpha (energy-preserving), combine variance, then convert back
    float baseR = saturate(baseRoughness);
    float baseR2 = baseR * baseR;
    float adjustedR = sqrt(baseR2 + normalVar);    
    return saturate(adjustedR);
}
#endif



/**
 * Values for the base color (SV_Target3) and material (SV_Target4) targets of [RenderTarget], read from frag after
 * ComputePbr(). tint is any color multiplied onto the lit result afterwards, coverage 0 marks pixels without a surface.
 * Material alpha is the relight coverage: 1 for PBR surfaces, 0 where nothing relightable was drawn. Unlike an id it
 * survives the MSAA resolve, which averages samples.
 */
void GetMaterialTargets(float3 tint, float coverage, out float4 baseColor, out float4 material)
{
    baseColor = float4(saturate(frag.albedo.rgb * BaseColor.rgb * Color.rgb * tint), saturate(frag.Occlusion));
    material = float4(saturate(frag.Roughness), saturate(frag.Metalness), saturate(Specular), saturate(coverage));
}

// based on https://github.com/Nadrin/PBR/blob/master/data/shaders/hlsl/pbr.hlsl
float4 ComputePbr()
{
    float3 N = frag.N;
    float3 V = frag.Lo;
    float3 Lo = frag.Lo;

    // Angle between surface normal and outgoing light direction.
    frag.cosLo = abs(dot(N, Lo));
    // frag.cosLo = saturate(dot(N, V)); // not abs
    // float3 Lr = 2.0 * frag.cosLo * N - V;

    // Specular reflection vector.
    float3 Lr = 2.0 * frag.cosLo * N - Lo;
    // return float4(Lr.xyz,1);

    // Fresnel reflectance at normal incidence (for metals use albedo color).
    float3 F0 = lerp(Fdielectric, frag.albedo.rgb, frag.Metalness);

    // Direct lighting calculation for analytical lights.
    float3 directLighting = 0.0;
    for (uint i = 0; i < (uint)ActiveLightCount; ++i)
    {
        float3 L;
        float3 Lradiance;
        EvaluateLight(Lights[i], frag.worldPosition, L, Lradiance);

        float NdotV = saturate(dot(N, V));
        float NdotL = saturate(dot(N, L));
        float3 H = normalize(L + V);
        float NdotH = saturate(dot(N, H));

        float3 F = fresnelSchlick(F0, saturate(dot(H, V)));
        float D = ndfGGX(NdotH, frag.Roughness);
        float G = gaSchlickGGX(NdotL, NdotV, frag.Roughness);

        float3 kd = lerp(1.0 - F, 0.0, frag.Metalness);
        float3 diffuseBRDF = kd * frag.albedo.rgb;
        float3 specularBRDF = ((F * D * G) / max(Epsilon, 4.0 * NdotL * NdotV)) * Specular;

        directLighting += (diffuseBRDF + specularBRDF) * Lradiance * NdotL;
    }

    // Ambient lighting (IBL).
    float3 ambientLighting = 0;
    {
        // Sample diffuse irradiance at normal direction.
        // float3 irradiance = 0;// irradianceTexture.Sample(TexSampler, N).rgb;
        uint width, height, levels;
        PrefilteredSpecular.GetDimensions(0, width, height, levels);
        float3 irradiance = PrefilteredSpecular.SampleLevel(WrappedSampler, N, 0.6 * levels).rgb;

        // Calculate Fresnel term for ambient lighting.
        // Since we use pre-filtered cubemap(s) and irradiance is coming from many directions
        // use cosLo instead of angle with light's half-vector (cosLh above).
        // See: https://seblagarde.wordpress.com/2011/08/17/hello-world/
        float3 F = fresnelSchlick(F0, frag.cosLo);

        // Get diffuse contribution factor (as with direct lighting).
        float3 kd = lerp(1.0 - F, 0.0, frag.Metalness);

        // Irradiance map contains exitant radiance assuming Lambertian BRDF, no need to scale by 1/PI here either.
        float3 diffuseIBL = kd * frag.albedo.rgb * irradiance;

        // Sample pre-filtered specular reflection environment at correct mipmap level.
        float3 specularIrradiance = PrefilteredSpecular.SampleLevel(WrappedSampler, Lr, frag.Roughness * levels).rgb;

        // Split-sum approximation factors for Cook-Torrance specular BRDF.
        float2 specularBRDF = BRDFLookup.SampleLevel(ClampedSampler, float2(frag.cosLo, frag.Roughness), 0).rg;

        // Total specular IBL contribution.
        float3 specularIBL = (F0 * specularBRDF.x + specularBRDF.y) * specularIrradiance;
        ambientLighting = (diffuseIBL + specularIBL) * frag.Occlusion;
    }

    // Final fragment color.
    float4 litColor = float4(directLighting + ambientLighting, 1.0) * BaseColor * Color;

    litColor += float4(EmissiveColorMap.Sample(WrappedSampler, frag.uv).rgb * EmissiveColor.rgb, 0);
    litColor.rgb = lerp(litColor.rgb, FogColor.rgb, frag.fog * FogColor.a);
    litColor.a *= frag.albedo.a;

    return litColor;
}