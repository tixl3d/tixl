#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"
#include "shared/blend-functions.hlsl"

cbuffer Params : register(b0)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer ParamConstants : register(b1)
{
    float MaxSteps;
    float StepSize;
    float MinDistance;
    float DistToColor;

    float4 Color;
    float4 AmbientOcclusion;

    float TextureScale;
    float AODistance;
    float NormalSamplingDistance;
}

cbuffer Transforms : register(b2)
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

// Context C Buffers
cbuffer FogParams : register(b3)
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

cbuffer PointLights : register(b4)
{
    PointLight Lights[8];
    uint ActiveLightCount;
}

cbuffer PbrParams : register(b5)
{
    float4 BaseColor;
    float4 EmissiveColor;
    float Roughness;
    float Specular2;
    float Metal;
    float BlendMode;
}

Texture2D<float4> BaseColorMap : register(t0);
Texture2D<float4> EmissiveColorMap : register(t1);
Texture2D<float4> RSMOMap : register(t2);
Texture2D<float4> NormalMap : register(t3);
Texture2D<float4> BRDFLookup : register(t4);
TextureCube<float4> PrefilteredSpecular : register(t5);
Texture2D<float4> BaseColorMap2 : register(t6);

sampler texSampler : register(s0);
sampler clampedSampler : register(s1);
//--------------------

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float3 viewDir : VPOS;
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

vsOutput vsMain4(uint vertexId : SV_VertexID)
{
    vsOutput output;
    float4 quadPos = float4(Quad[vertexId], 1);
    float2 texCoord = quadPos.xy * float2(0.5, -0.5) + 0.5;
    output.texCoord = texCoord;
    output.position = quadPos;
    float4x4 ViewToWorld = ClipSpaceToWorld; // CameraToWorld ;

    float4 viewTNearFragPos = float4(texCoord.x * 2.0 - 1.0, -texCoord.y * 2.0 + 1.0, 0.0, 1.0);
    float4 worldTNearFragPos = mul(viewTNearFragPos, ViewToWorld);
    worldTNearFragPos /= worldTNearFragPos.w;

    float4 viewTFarFragPos = float4(texCoord.x * 2.0 - 1.0, -texCoord.y * 2.0 + 1.0, 1.0, 1.0);
    float4 worldTFarFragPos = mul(viewTFarFragPos, ViewToWorld);
    worldTFarFragPos /= worldTFarFragPos.w;

    output.worldTViewDir = normalize(worldTFarFragPos.xyz - worldTNearFragPos.xyz);
    output.worldTViewPos = worldTNearFragPos.xyz;

    output.viewDir = -normalize(float3(CameraToWorld._31, CameraToWorld._32, CameraToWorld._33));

    return output;
}

//--- Field functions -----------------------
/*{FIELD_FUNCTIONS}*/
//-------------------------------------------

float GetDistance(float3 pos)
{
    // pos = mul(float4(pos.xyz,1), ObjectToWorld).xyz;
    return /*{FIELD_CALL}*/ 0;
}
//---------------------------------------------------

float3 GetNormal(float3 p, float offset)
{
    // float dt = .01;
    // float3 n = float3(GetDistance(p + float3(dt, 0, 0)),
    //                   GetDistance(p + float3(0, dt, 0)),
    //                   GetDistance(p + float3(0, 0, dt))) -
    //            GetDistance(p);
    // return normalize(n);
    //NormalSamplingDistance;

    return normalize(
    GetDistance(p + float3( NormalSamplingDistance, -NormalSamplingDistance, -NormalSamplingDistance)) * float3( 1, -1, -1) +
    GetDistance(p + float3(-NormalSamplingDistance,  NormalSamplingDistance, -NormalSamplingDistance)) * float3(-1,  1, -1) +
    GetDistance(p + float3(-NormalSamplingDistance, -NormalSamplingDistance,  NormalSamplingDistance)) * float3(-1, -1,  1) +
    GetDistance(p + float3( NormalSamplingDistance,  NormalSamplingDistance,  NormalSamplingDistance)) * float3( 1,  1,  1)
);    
}

float ComputeAO(float3 aoposition, float3 aonormal, float aodistance, float aoiterations, float aofactor)
{
    float ao = 0.0;
    float k = aofactor;
    aodistance /= aoiterations;
    for (int i = 1; i < 4; i += 1)
    {
        ao += (i * aodistance - GetDistance(aoposition + aonormal * i * aodistance)) / pow(2, i);
    }
    return 1.0 - k * ao;
}

static float MAX_DIST = 300;


struct PSOutput
{
    float4 color : SV_Target;
    float depth : SV_Depth;
};

float ComputeDepthFromViewZ(float viewZ)
{
    float4 clipPos = mul(float4(0,0,viewZ,1), CameraToClipSpace);
    return clipPos.z / clipPos.w;
}


// Define mapping type constants
#define MAP_YZ 0          // Default
#define MAP_TRIPLANAR 1
#define MAP_XY 2
#define MAP_XZ 3
#define MAP_POLAR 4

    float2 GetUVMapping(float3 p, float3 absN, int mappingType)
{
    
    switch (mappingType)
    {
        case MAP_TRIPLANAR:
            p.yz *= -1;
            return (absN.x > absN.y && absN.x > absN.z) ? (p.zy / TextureScale)+.5 : 
                   (absN.y > absN.z) ? (p.zx / TextureScale)+.5 : (p.xy / TextureScale)+.5;
        
        case MAP_XY:
            return (-p.xy+.5)/TextureScale;
            
        case MAP_XZ:
            return -p.xz+.5;
            
        case MAP_POLAR:
            return (float2(atan2(p.x, p.z)/6.2832, p.y/3.0)+.5);
            
        default: // YZ mapping
            return p.yz;
    }
}


PSOutput psMain(vsOutput input)
{
    float3 eye = input.worldTViewPos;

    // Early test. This will lead to z-problems later
    // eye = mul(float4(eye,1), ObjectToWorld).xyz;
    float3 p = eye;
    float3 tmpP = p;
    float3 dp = normalize(input.worldTViewDir);
    // dp = mul(float4(dp,0), ObjectToWorld).xyz;

    float totalD = 0.0;
    float D = 3.4e38;
    D = StepSize;
    float extraD = 0.0;
    float lastD;
    int steps;
    int maxSteps = (int)(MaxSteps - 0.5);

    // Simple iterator
    for (steps = 0; steps < maxSteps && abs(D) > MinDistance; steps++)
    {
        D = GetDistance(p);
        p += dp * D;
    }

    p += totalD * dp;

    // Color the surface with Blinn-Phong shading, ambient occlusion and glow.
    float3 col = 0;
    float a = 1;

    float3 normal = 0;

    // We've got a hit or we're not sure.
    if (D < MAX_DIST)
    {
        normal = normalize(GetNormal(p, D));

        //col = Color.rgb;
        // We've gone through all steps, but we haven't hit anything.
        // Mix in the background color.
        if (D > MinDistance)
        {
            a = 1 - clamp(log(D / MinDistance) * DistToColor, 0.0, 1.0); // Clarify if this is actually useful
        }
    }
    else
    {
        a = 0;
    }

    // Discard transparent fragments...
    if (a < 0.1)
        discard;

    // PBR shading -------------------------------------------------------------------------

    // Tri-planar mappping
    float3 absN = abs(normal);
 
    #if MAPPING_TRIPLANAR
        float2 uv = GetUVMapping(p, absN, MAP_TRIPLANAR);
    #elif MAPPING_XY
        float2 uv = GetUVMapping(p, absN, MAP_XY);
    #elif MAPPING_XZ
        float2 uv = GetUVMapping(p, absN, MAP_XZ);
    #elif MAPPING_POLAR
        float2 uv = GetUVMapping(p, absN, MAP_POLAR);
    #else
        float2 uv = GetUVMapping(p, absN, MAP_YZ); // Default
    #endif

    #if MAPPING_TRIPLANAR2
        float2 uv2 = GetUVMapping(p, absN, MAP_TRIPLANAR);
    #elif MAPPING_XY2
        float2 uv2 = GetUVMapping(p, absN, MAP_XY);
    #elif MAPPING_XZ2
        float2 uv2 = GetUVMapping(p, absN, MAP_XZ);
    #elif MAPPING_POLAR
        float2 uv2 = GetUVMapping(p, absN, MAP_POLAR);
    #else
        float2 uv2 = GetUVMapping(p, absN, MAP_YZ); // Default
    #endif

    float4 albedo = BaseColorMap.Sample(texSampler, uv);

    float4 albedo2 = BaseColorMap2.Sample(texSampler, uv2);
    albedo = BlendColors(albedo, albedo2, (int)BlendMode);

    float4 roughnessMetallicOcclusion = RSMOMap.Sample(texSampler, uv);
    float roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    float metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    float occlusion = roughnessMetallicOcclusion.z;

    float3 Lo = -dp;

    // Angle between surface normal and outgoing light direction.
    float cosLo = max(0.0, dot(normal, Lo));

    // Specular reflection vector.
    float3 Lr = 2.0 * cosLo * normal - Lo;

    // Fresnel reflectance at normal incidence (for metals use albedo color).
    float3 F0 = lerp(Fdielectric.xxx, albedo.rgb, metalness).rgb;

    // Direct lighting calculation for analytical lights.
    float3 directLighting = 0.0;

    for (uint i = 0; i < ActiveLightCount; ++i)
    {
        if (i < 0)
            continue;

        float3 Li = Lights[i].position - p; //- Lights[i].direction;
        float distance = length(Li);
        float intensity = Lights[i].intensity / (pow(distance / Lights[i].range, Lights[i].decay) + 1);
        float3 Lradiance = Lights[i].color.rgb * intensity; // Lights[i].radiance;

        // Half-vector between Li and Lo.
        float3 Lh = normalize(Li + Lo);

        // Calculate angles between surface normal and various light vectors.
        float cosLi = max(0.0, dot(normal, Li));
        float cosLh = max(0.0, dot(normal, Lh));

        // Calculate Fresnel term for direct lighting.
        float3 F = fresnelSchlick(F0, max(0.0, dot(Lh, Lo)));

        // Calculate normal distribution for specular BRDF.
        float D = ndfGGX(cosLh, roughness);
        // Calculate geometric attenuation for specular BRDF.
        float G = gaSchlickGGX(cosLi, cosLo, roughness);

        // Diffuse scattering happens due to light being refracted multiple times by a dielectric medium.
        // Metals on the other hand either reflect or absorb energy, so diffuse contribution is always zero.
        // To be energy conserving we must scale diffuse BRDF contribution based on Fresnel factor & metalness.
        float3 kd = lerp(float3(1, 1, 1), float3(0, 0, 0), metalness);
        // return float4(F, 1);

        // Lambert diffuse BRDF.
        // We don't scale by 1/PI for lighting & material units to be more convenient.
        // See: https://seblagarde.wordpress.com/2012/01/08/pi-or-not-to-pi-in-game-lighting-equation/
        float3 diffuseBRDF = kd * albedo.rgb;

        // Cook-Torrance specular microfacet BRDF.
        float3 specularBRDF = ((F * D * G) / max(Epsilon, 4.0 * cosLi * cosLo)) * Color.rgb;

        // Total contribution for this light.
        directLighting += (diffuseBRDF + specularBRDF) * Lradiance * cosLi;
    }

    // Ambient lighting (IBL).
    float3 ambientLighting = 0;
    {
        // Sample diffuse irradiance at normal direction.
        uint width, height, levels;
        PrefilteredSpecular.GetDimensions(0, width, height, levels);
        float3 irradiance = PrefilteredSpecular.SampleLevel(texSampler, normal, 0.6 * levels).rgb;

        // Calculate Fresnel term for ambient lighting.
        // Since we use pre-filtered cubemap(s) and irradiance is coming from many directions
        // use cosLo instead of angle with light's half-vector (cosLh above).
        // See: https://seblagarde.wordpress.com/2011/08/17/hello-world/
        float3 F = fresnelSchlick(F0, cosLo);

        // Get diffuse contribution factor (as with direct lighting).
        float3 kd = lerp(1.0 - F, 0.0, metalness);

        // Irradiance map contains exitant radiance assuming Lambertian BRDF, no need to scale by 1/PI here either.
        float3 diffuseIBL = kd * albedo.rgb * irradiance;

        // Sample pre-filtered specular reflection environment at correct mipmap level.
        float3 specularIrradiance = PrefilteredSpecular.SampleLevel(texSampler, Lr, roughness * levels).rgb;

        // Split-sum approximation factors for Cook-Torrance specular BRDF.
        float2 specularBRDF = BRDFLookup.SampleLevel(clampedSampler, float2(cosLo, roughness), 0).rg;

        // Total specular IBL contribution.
        float3 specularIBL = (F0 * specularBRDF.x + specularBRDF.y) * specularIrradiance;
        ambientLighting = (diffuseIBL + specularIBL) * occlusion;
    }

    // Final fragment color.
    float4 litColor = float4(directLighting + ambientLighting, 1.0) * BaseColor * Color; // TODO Add parameter * Color;

    // Fog
    float depth = dot(eye - p, -input.viewDir);
    if (FogDistance > 0)
    {
        float fog = pow(saturate(depth / FogDistance), FogBias);
        litColor.rgb = lerp(litColor.rgb, FogColor.rgb, fog * FogColor.a);
    }

    litColor += float4(EmissiveColorMap.Sample(texSampler, uv).rgb * EmissiveColor.rgb, 0);
    litColor.a *= albedo.a;
    litColor.rgb = lerp(AmbientOcclusion.rgb, litColor.rgb, ComputeAO(p, normal, AODistance, 3, AmbientOcclusion.a));

    PSOutput result;
    result.color = clamp(litColor, 0, float4(1000,1000,1000,1));

    float viewZ = mul(float4(p, 1), WorldToCamera).z;
    //result.color.a  = 1;
    result.depth =  ComputeDepthFromViewZ(viewZ);
    return result;
}
