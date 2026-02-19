// Physically Based Rendering
// Adapted from  2017-2018 Michał Siejak
// Physically Based shading model: Lambetrtian diffuse BRDF + Cook-Torrance microfacet specular BRDF + IBL for ambient.
// This implementation is based on "Real Shading in Unreal Engine 4" SIGGRAPH 2013 course notes by Epic Games.
// See: http://blog.selfshadow.com/publications/s2013-shading-course/karis/s2013_pbs_epic_notes_v2.pdf

#ifndef __pbr
#define __pbr

#ifndef PI
#define PI 3.141592
#endif

#ifndef Epsilon
#define Epsilon 0.00001
#endif

struct PbrVertex
{
    float3 Position;
    float3 Normal;
    float3 Tangent;
    float3 Bitangent;
    float2 TexCoord;
    float2 TexCoord2;
    float Selected;
    float3 ColorRGB;
};

// Constant normal incidence Fresnel factor for all dielectrics.
#define Fdielectric 0.04

// GGX/Towbridge-Reitz normal distribution function.
// Uses Disney's reparametrization of alpha = roughness^2.
float ndfGGX(float cosLh, float roughness)
{
    float alpha = roughness * roughness;
    float alphaSq = alpha * alpha;

    float denom = (cosLh * cosLh) * (alphaSq - 1.0) + 1.0;
    return alphaSq / (PI * denom * denom);
}

// Single term for separable Schlick-GGX below.
float gaSchlickG1(float cosTheta, float k)
{
    return cosTheta / (cosTheta * (1.0 - k) + k);
}

// Schlick-GGX approximation of geometric attenuation function using Smith's method.
float gaSchlickGGX(float cosLi, float cosLo, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0; // Epic suggests using this roughness remapping for analytic lights.
    return gaSchlickG1(cosLi, k) * gaSchlickG1(cosLo, k);
}

// Shlick's approximation of the Fresnel factor.
float3 fresnelSchlick(float3 F0, float cosTheta)
{
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

// Returns number of mipmap levels for specular IBL environment map.
uint querySpecularTextureLevels(Texture2D specularTexture)
{
    uint width, height, levels;
    specularTexture.GetDimensions(0, width, height, levels);
    return levels;
}

#ifndef __frag_static

struct FragmentMaterial
{
    float Roughness;
    float Metalness;
    float Occlusion;
    float cosLo;
    float3 worldPosition;
    float4 albedo;
    float2 uv;
    float3 N;
    float fog;
    float3 Lo;
};

static FragmentMaterial frag;
#define __frag_static
#endif

#endif