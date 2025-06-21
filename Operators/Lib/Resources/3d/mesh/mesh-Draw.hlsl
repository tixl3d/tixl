#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"

cbuffer Transforms : register(b0)
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

cbuffer Params : register(b1)
{
    float4 Color;
    float AlphaCutOff;
    float Debug;
};

cbuffer FogParams : register(b2)
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

cbuffer PointLights : register(b3)
{
    PointLight Lights[8];
    int ActiveLightCount;
}

cbuffer PbrParams : register(b4)
{
    float4 BaseColor;
    float4 EmissiveColor;
    float Roughness;
    float Specular;
    float Metal;
}

cbuffer Params : register(b5)
{
    /*{FLOAT_PARAMS}*/
}

struct psInput
{
    float2 texCoord : TEXCOORD;
    float4 pixelPosition : SV_POSITION;
    float3 worldPosition : POSITION;
    float3x3 tbnToWorld : TBASIS;
    float fog : VPOS;
};

sampler TexSampler : register(s0);
sampler LinearSampler : register(s1);
sampler ClampedSampler : register(s2);

StructuredBuffer<PbrVertex> PbrVertices : register(t0);
StructuredBuffer<int3> FaceIndices : register(t1);

Texture2D<float4> BaseColorMap : register(t2);
Texture2D<float4> EmissiveColorMap : register(t3);
Texture2D<float4> RSMOMap : register(t4);
Texture2D<float4> NormalMap : register(t5);

TextureCube<float4> PrefilteredSpecular : register(t6);
Texture2D<float4> BRDFLookup : register(t7);

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;

    int faceIndex = id / 3; //  (id % verticesPerInstance) / 3;
    int faceVertexIndex = id % 3;

    PbrVertex vertex = PbrVertices[FaceIndices[faceIndex][faceVertexIndex]];

    float4 posInObject = float4(vertex.Position, 1);

    float4 posInClipSpace = mul(posInObject, ObjectToClipSpace);
    output.pixelPosition = posInClipSpace;

    float2 uv = vertex.TexCoord;
    output.texCoord = float2(uv.x, 1 - uv.y);

    // Pass tangent space basis vectors (for normal mapping).
    float3x3 TBN = float3x3(vertex.Tangent, vertex.Bitangent, vertex.Normal);
    TBN = mul(TBN, (float3x3)ObjectToWorld);

    output.tbnToWorld = float3x3(
        normalize(TBN._m00_m01_m02),
        normalize(TBN._m10_m11_m12),
        normalize(TBN._m20_m21_m22));

    output.worldPosition = mul(posInObject, ObjectToWorld).xyz;

    // Fog
    if (FogDistance > 0)
    {
        float4 posInCamera = mul(posInObject, ObjectToCamera);
        float fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
        output.fog = fog;
    }

    return output;
}

struct FragmentMaterial
{
    float Roughness;
    float Metalness;
    float Occlusion;
    float cosLo;
};

static FragmentMaterial frag;

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Additional Resources ==========================================
/*{RESOURCES(t8)}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------

//-------------------------------------------------------------------
inline float4 GetField(float4 p)
{
#ifndef USE_WORLDSPACE
    p.xyz = mul(float4(p.xyz, 1), WorldToObject).xyz;
#endif
    float4 f = 1;
    /*{FIELD_CALL}*/

#ifndef USE_WORLDSPACE
    float uniformScale = length(ObjectToWorld[0].xyz);
    f.w *= uniformScale;
#endif
    return f;
}

float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}
//===================================================================

// based on https://github.com/Nadrin/PBR/blob/master/data/shaders/hlsl/pbr.hlsl
float4 psMain(psInput pin) : SV_TARGET
{
    // Sample input textures to get shading model params.
    float4 albedo = BaseColorMap.Sample(TexSampler, pin.texCoord);
    if (AlphaCutOff > 0 && albedo.a < AlphaCutOff)
    {
        discard;
    }

    float4 roughnessMetallicOcclusion = RSMOMap.Sample(TexSampler, pin.texCoord);
    frag.Roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    frag.Metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    frag.Occlusion = roughnessMetallicOcclusion.z;

    // Outgoing light direction (vector from world-space fragment position to the "eye").
    float4 eyePosition = mul(float4(0, 0, 0, 1), CameraToWorld);
    float3 Lo = normalize(eyePosition.xyz - pin.worldPosition);

    // Get current fragment's normal and transform to world space.
    float4 normalMap = NormalMap.Sample(TexSampler, pin.texCoord);

    float3 N = normalize(2.0 * normalMap.rgb - 1.0);
    N = normalize(mul(N, pin.tbnToWorld));

    // Angle between surface normal and outgoing light direction.
    frag.cosLo = abs(dot(N, Lo));

    // Specular reflection vector.
    float3 Lr = 2.0 * frag.cosLo * N - Lo;
    // return float4(Lr.xyz,1);

    // Fresnel reflectance at normal incidence (for metals use albedo color).
    float3 F0 = lerp(Fdielectric, albedo.rgb, frag.Metalness);

    // Direct lighting calculation for analytical lights.
    float3 directLighting = 0.0;
    for (uint i = 0; i < (uint)ActiveLightCount; ++i)
    {
        float3 Li = Lights[i].position - pin.worldPosition; //- Lights[i].direction;
        float distance = length(Li);
        float intensity = Lights[i].intensity / (pow(distance / Lights[i].range, Lights[i].decay) + 1);
        float3 Lradiance = Lights[i].color.rgb * intensity; // Lights[i].radiance;

        // Half-vector between Li and Lo.
        float3 Lh = normalize(Li + Lo);

        // Calculate angles between surface normal and various light vectors.
        float cosLi = max(0.0, dot(N, Li));
        float cosLh = max(0.0, dot(N, Lh));

        // Calculate Fresnel term for direct lighting.
        float3 F = fresnelSchlick(F0, max(0.0, dot(Lh, Lo)));

        // Calculate normal distribution for specular BRDF.
        float D = ndfGGX(cosLh, frag.Roughness);
        // Calculate geometric attenuation for specular BRDF.
        float G = gaSchlickGGX(cosLi, frag.cosLo, frag.Roughness);

        // Diffuse scattering happens due to light being refracted multiple times by a dielectric medium.
        // Metals on the other hand either reflect or absorb energy, so diffuse contribution is always zero.
        // To be energy conserving we must scale diffuse BRDF contribution based on Fresnel factor & metalness.
        float3 kd = lerp(float3(1, 1, 1), float3(0, 0, 0), frag.Metalness);
        // return float4(F, 1);

        // Lambert diffuse BRDF.
        // We don't scale by 1/PI for lighting & material units to be more convenient.
        // See: https://seblagarde.wordpress.com/2012/01/08/pi-or-not-to-pi-in-game-lighting-equation/
        float3 diffuseBRDF = kd * albedo.rgb;

        // Cook-Torrance specular microfacet BRDF.
        float3 specularBRDF = ((F * D * G) / max(Epsilon, 4.0 * cosLi * frag.cosLo)) * Specular;

        // Total contribution for this light.
        directLighting += (diffuseBRDF + specularBRDF) * Lradiance * cosLi;
    }

    // Ambient lighting (IBL).
    float3 ambientLighting = 0;
    {
        // Sample diffuse irradiance at normal direction.
        // float3 irradiance = 0;// irradianceTexture.Sample(TexSampler, N).rgb;
        uint width, height, levels;
        PrefilteredSpecular.GetDimensions(0, width, height, levels);
        float3 irradiance = PrefilteredSpecular.SampleLevel(LinearSampler, N, 0.6 * levels).rgb;

        // Calculate Fresnel term for ambient lighting.
        // Since we use pre-filtered cubemap(s) and irradiance is coming from many directions
        // use cosLo instead of angle with light's half-vector (cosLh above).
        // See: https://seblagarde.wordpress.com/2011/08/17/hello-world/
        float3 F = fresnelSchlick(F0, frag.cosLo);

        // Get diffuse contribution factor (as with direct lighting).
        float3 kd = lerp(1.0 - F, 0.0, frag.Metalness);

        // Irradiance map contains exitant radiance assuming Lambertian BRDF, no need to scale by 1/PI here either.
        float3 diffuseIBL = kd * albedo.rgb * irradiance;

        // Sample pre-filtered specular reflection environment at correct mipmap level.
        float3 specularIrradiance = PrefilteredSpecular.SampleLevel(LinearSampler, Lr, frag.Roughness * levels).rgb;

        // Split-sum approximation factors for Cook-Torrance specular BRDF.
        float2 specularBRDF = BRDFLookup.SampleLevel(ClampedSampler, float2(frag.cosLo, frag.Roughness), 0).rg;

        // Total specular IBL contribution.
        float3 specularIBL = (F0 * specularBRDF.x + specularBRDF.y) * specularIrradiance;
        ambientLighting = (diffuseIBL + specularIBL) * frag.Occlusion;
    }

    // Final fragment color.
    float4 litColor = float4(directLighting + ambientLighting, 1.0) * BaseColor * Color;
    litColor += float4(EmissiveColorMap.Sample(TexSampler, pin.texCoord).rgb * EmissiveColor.rgb, 0);
    litColor.rgb = lerp(litColor.rgb, FogColor.rgb, pin.fog * FogColor.a);
    litColor.a *= albedo.a;
    litColor.rgb *= GetField(float4(pin.worldPosition.xyz, 0)).rgb;
    return litColor;
}
