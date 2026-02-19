#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"

static const float3 Corners[] = 
{
  float3(0, -1, 0),
  float3(1, -1, 0), 
  float3(1,  1, 0), 
  float3(1,  1, 0), 
  float3(0,  1, 0), 
  float3(0, -1, 0),  
};

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
    float Width;
    float Spin;
    float Twist;
    float TextureMode;
    float2 TextureRange;    
    float UseWAsWeight;
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

struct psInput
{
    float2 texCoord : TEXCOORD;
    float4 pixelPosition : SV_POSITION;
    float3 worldPosition : POSITION;
    float3x3 tbnToWorld : TBASIS;    
    float fog:VPOS;
    float4 color: COLOR;
};

sampler texSampler : register(s0);
sampler clampedSampler : register(s1);

StructuredBuffer<LegacyPoint> Points : t0;
//Texture2D<float4> texture2 : register(t1);

Texture2D<float4> BaseColorMap : register(t1);
Texture2D<float4> EmissiveColorMap : register(t2);
Texture2D<float4> RSMOMap : register(t3);
Texture2D<float4> NormalMap : register(t4);

TextureCube<float4> PrefilteredSpecular: register(t5);
Texture2D<float4> BRDFLookup : register(t6);

psInput vsMain(uint id: SV_VertexID)
{
    uint pointCount, pointStride;
    Points.GetDimensions(pointCount, pointStride);

    psInput output;
    float discardFactor = 1;
    int quadIndex = id % 6;
    int particleId = id / 6;

    float3 cornerFactors = Corners[quadIndex];
    float f = (float)(particleId + cornerFactors.x)  / clamp(pointCount - 1, 1,100000);

    int offset = cornerFactors.x < 0.5 ? 0 : 1; 
    LegacyPoint p = Points[particleId+offset];

    float spinRad = (Spin + Twist *f) * 3.141578/180;
    //float3 side = float3(0, cos(spinRad), sin(spinRad)) * cornerFactors.y;
    float3 side = float3(cos(spinRad), 0, sin(spinRad)) * cornerFactors.y;

    float WidthFactor = UseWAsWeight || isnan(p.W)> 0.5 ? p.W  : 1;
    float3 widthV = qRotateVec3(side, p.Rotation) * Width * WidthFactor;
    float3 pInObject = p.Position + widthV;

    float3 normalTwisted =  float3(0, cos(spinRad + 3.141578/2), sin(spinRad + 3.141578/2));
    float3 normal = normalize(qRotateVec3(normalTwisted, p.Rotation));
    float4 normalInScreen = mul(float4(normal,0), ObjectToClipSpace);

    output.texCoord = float2(cornerFactors.x , cornerFactors.y /2 +0.5);
    if(TextureMode < 0.5) {
        output.texCoord = float2( f * (TextureRange.y - TextureRange.x) + TextureRange.x ,  cornerFactors.y /2 +0.5);
    }
    else if (TextureMode < 1.5) {
        output.texCoord = float2( f * TextureRange.y + TextureRange.x ,  cornerFactors.y /2 +0.5);        
    }
    else if (TextureMode < 2.5) {
        output.texCoord += TextureRange;
    }
    else  {
        output.texCoord.x = p.W;
    }

    // Pass tangent space basis vectors (for normal mapping).
    float3x3 TBN = float3x3(
        normalize(qRotateVec3(float3(1,0,0), p.Rotation)), //  vertex.Bitangent, 
        side, 
        normal
        );
        
    TBN = mul(TBN, (float3x3)ObjectToWorld);
    output.tbnToWorld = TBN;

    output.worldPosition =  mul(float4(pInObject,0), ObjectToWorld); 

    float4 pInScreen  = mul(float4(pInObject,1), ObjectToClipSpace);

    // float3 lightDirection = float3(1.2, 1, -0.1);
    // float phong = pow(  abs(dot(normal,lightDirection )),1);
    
    output.pixelPosition = pInScreen;

    // Fog
    float4 posInCamera = mul(float4(pInObject,1), ObjectToCamera);
    output.fog = pow(saturate(-posInCamera.z/FogDistance), FogBias);
    output.color = Color * p.Color;
    return output;    
}


float4 psMain(psInput pin) : SV_TARGET
{
    // Sample input textures to get shading model params.
    float4 albedo = BaseColorMap.Sample(texSampler, pin.texCoord).rgba;
    float4 roughnessMetallicOcclusion = RSMOMap.Sample(texSampler, pin.texCoord);
    float roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    float metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    float occlusion = roughnessMetallicOcclusion.z;

    // Outgoing light direction (vector from world-space fragment position to the "eye").
    float4 eyePosition = mul(float4(0, 0, 0, 1), CameraToWorld);
    float3 Lo = normalize(eyePosition.xyz - pin.worldPosition);

    // Get current fragment's normal and transform to world space.
    float4 normalMap = NormalMap.Sample(texSampler, pin.texCoord);
    
    float3 N = normalize(2.0 * normalMap.rgb - 1.0);
    N = normalize(mul(N, pin.tbnToWorld));

    // Angle between surface normal and outgoing light direction.
    float cosLo = abs( dot(N, Lo));

    // Specular reflection vector.
    float3 Lr = 2.0 * cosLo * N - Lo;

    // Fresnel reflectance at normal incidence (for metals use albedo color).
    float3 F0 = lerp(Fdielectric, albedo, metalness);

    // Direct lighting calculation for analytical lights.
    float3 directLighting = 0.0;
    for (uint i = 0; i < ActiveLightCount; ++i)
    {
        float3 Li = Lights[i].position - pin.worldPosition; //- Lights[i].direction;
        float distance = length(Li);
        float intensity = Lights[i].intensity / (pow(distance/Lights[i].range, Lights[i].decay) + 1);
        float3 Lradiance = Lights[i].color * intensity; // Lights[i].radiance;

        // Half-vector between Li and Lo.
        float3 Lh = normalize(Li + Lo);

        // Calculate angles between surface normal and various light vectors.
        float cosLi = max(0.0, dot(N, Li));
        float cosLh = max(0.0, dot(N, Lh));

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
        float3 specularBRDF = ((F * D * G) / max(Epsilon, 4.0 * cosLi * cosLo)) * Specular;

        // Total contribution for this light.
        directLighting += (diffuseBRDF + specularBRDF) * Lradiance * cosLi;
    }

    // Ambient lighting (IBL).
    float3 ambientLighting = 0;
    {
        // Sample diffuse irradiance at normal direction.
        // float3 irradiance = 0;// irradianceTexture.Sample(texSampler, N).rgb;
        uint width, height, levels;
        PrefilteredSpecular.GetDimensions(0, width, height, levels);
        float3 irradiance = PrefilteredSpecular.SampleLevel(texSampler, N, 0.6 * levels).rgb;

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
		float2 specularBRDF = BRDFLookup.SampleLevel(clampedSampler, float2(cosLo, roughness),0).rg;
        
		// Total specular IBL contribution.
		float3 specularIBL = (F0 * specularBRDF.x + specularBRDF.y) * specularIrradiance;
        ambientLighting = (diffuseIBL + specularIBL) * occlusion;
    }

    // Final fragment color.
    float4 litColor = float4(directLighting + ambientLighting, 1.0) * BaseColor * pin.color;
    litColor += float4(EmissiveColorMap.Sample(texSampler, pin.texCoord).rgb * EmissiveColor.rgb, 0);
    litColor.rgb = lerp(litColor.rgb, FogColor.rgb, pin.fog * FogColor.a);
    litColor.a *= albedo.a;
    return litColor;
}