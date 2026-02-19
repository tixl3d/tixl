#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"
#include "shared/hash-functions.hlsl"

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
    float Scale;
    float3 Stretch;

    float3 Offset;
    float OrientationMode;

    float Rotate;
    float3 RotationAxis;

    float Randomize;
    float RandomPhase;
    float RandomRotate;
    float __padding0;

    float3 RandomPosition;
    float RandomScale;

    float3 RandomStretch;
    float __padding3;

    float4 Color;

    float ColorVariationMode;
    float ScaleDistribution;
    float SpreadLength;
    float SpreadPhase;

    float SpreadPingPong;
    float SpreadRepeat;
    float2 AtlasSize; // TODO: Remove

    float TextureAtlasMode; // TODO: Remove
    float FxTextureMode;
    float AlphaCutOff;
    float IsFxTextureConnected;

    float4 FxTextureAmount;

    float UseRotationAsRgba;
    float UseWFoScale;
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
    float4 color: COLOR;
    float4 pixelPosition : SV_POSITION;
    float3 worldPosition : POSITION;
    float fog : VPOS;
    float3x3 tbnToWorld : TBASIS;
    float2 texCoord : TEXCOORD;
};

sampler texSampler : register(s0);
sampler clampedSampler : register(s1);

StructuredBuffer<PbrVertex> PbrVertices : t0;
StructuredBuffer<int3> FaceIndices : t1;
StructuredBuffer<LegacyPoint> Points : t2;


Texture2D<float4> BaseColorMap : register(t3);
Texture2D<float4> EmissiveColorMap : register(t4);
Texture2D<float4> RSMOMap : register(t5);
Texture2D<float4> NormalMap : register(t6);
TextureCube<float4> PrefilteredSpecular : register(t7);
Texture2D<float4> BRDFLookup : register(t8);

Texture2D<float4> FxTexture : register(t9);
Texture2D<float4> ColorOverW : register(t10);
Texture2D<float4> SizeOverW : register(t11);


inline float GetUFromMode(float mode, int id, float f, float4 scatter, float w, float fog)
{
    switch ((int)(mode + 0.5))
    {

    case 0:
        return scatter.w;

    case 1:
        return hash11u(id);

    case 2:
        float f1 = (f + SpreadPhase) / SpreadLength;
        f1 = SpreadRepeat > 0.5 ? fmod(f1, 1) : f1;
        return SpreadPingPong > 0.5 ? (1 - abs(f1 * 2 - 1)) : f1;

    case 3:
        float w1 = (w + SpreadPhase) / SpreadLength;
        w1 = SpreadRepeat > 0.5 ? fmod(w1, 1) : w1;
        return SpreadPingPong > 0.5 ? (1 - abs(w1 * 2 - 1)) : w1;

    default:
        return fog;
    }
}

#define LimitScale(s)  ((s) > 1 ? s: 1/(2-(s)))


psInput vsMain(uint id : SV_VertexID)
{
    // SETUP ----------------------------------------------------------
    psInput output;

    uint faceCount, meshStride;
    FaceIndices.GetDimensions(faceCount, meshStride);

    int verticesPerInstance = faceCount * 3;

    int faceIndex = (id % verticesPerInstance) / 3;
    int faceVertexIndex = id % 3;

    uint pointCount, instanceStride;
    Points.GetDimensions(pointCount, instanceStride);

    int pointId = id / verticesPerInstance;

    LegacyPoint _p = Points[pointId];

    float4 pRotation = normalize(_p.Rotation); 
    float4 pPosition = float4(_p.Position,1);
    float pW = _p.W;

    // SETUP SEEDS ----------------------------------------------------------

    float f = pointId / (float)pointCount;

    float phase = RandomPhase + 133.1123 * f;
    int phaseId = (int)phase; 

    float4 normalizedScatter = lerp(hash41u(pointId * 12341 + phaseId),
                                    hash41u(pointId * 12341 + phaseId + 1),
                                    smoothstep(0, 1,
                                               phase - phaseId));

    float3 scatterForScale = normalizedScatter.xyx * 2 - 1;
    //scatterForScale = scatterForScale < 1 ? 1 / scatterForScale : scatterForScale;  

    // ------------------------------------------------------

    output.fog = 0;

    PbrVertex vertex = PbrVertices[FaceIndices[faceIndex][faceVertexIndex]];
    
    //float4 pInCamera = mul(posInObject, ObjectToCamera);

    float4 pInCamera = mul(pPosition, ObjectToCamera);
    output.fog = pow(saturate(-pInCamera.z / FogDistance), FogBias);

    // COLOR + FX TEXTURE -----------------------------

    float4 colorFromPoint = (UseRotationAsRgba > 0.5) ? pRotation : 1;

    float colorFxU = GetUFromMode(ColorVariationMode, pointId, f, normalizedScatter, pW, output.fog);
    output.color = Color * ColorOverW.SampleLevel(clampedSampler, float2(colorFxU, 0), 0) * colorFromPoint;

    float adjustedRotate = Rotate;
    float adjustedScale = Scale;
    float adjustedRandomize = Randomize;

    if (IsFxTextureConnected)
    {
        float4 centerPos = mul(float4(pInCamera.xyz, 1), CameraToClipSpace);
        centerPos.xyz /= centerPos.w;

        float4 fxColor = FxTexture.SampleLevel(clampedSampler, (centerPos.xy * float2(1, -1) + 1) / 2, 0);

        if(FxTextureMode < 0.5) 
        {
            output.color *= fxColor;
        }
        else {
            adjustedRotate += FxTextureAmount.r * fxColor.r * fxColor.a * 360;
            adjustedScale += FxTextureAmount.g * fxColor.g * fxColor.a;
            adjustedRandomize += FxTextureAmount.b * fxColor.b * fxColor.a;
        }
    }

    // Scale and stretch
    float scaleFxU = GetUFromMode(ScaleDistribution, pointId, f, normalizedScatter, pW, output.fog);
    float scaleFromCurve = SizeOverW.SampleLevel(clampedSampler, float2(scaleFxU, 0), 0).r;
    float hideUndefinedPoints = isnan(pW) ? 0 : (UseWFoScale > 0.5 ? max(pW, 0) : 1 );
    
    float r= (RandomScale * scatterForScale.y *adjustedRandomize + 1);
    r = LimitScale(r);
    float computedScale = adjustedScale * r * scaleFromCurve * hideUndefinedPoints;

    // VERTEX POSITION -------------------------------------------------------------------

    float4 vInObject = float4(vertex.Position, 1);

    vInObject.xyz *=   computedScale * Scale * Stretch * LimitScale(RandomStretch * scatterForScale + 1);

    float3 randomOffset = qRotateVec3((normalizedScatter.xyz - 0.5) * 2 * RandomPosition * Randomize, pRotation);
    vInObject.xyz += randomOffset;
    vInObject.xyz += Offset;


    float4x4 orientationMatrix = transpose(qToMatrix(normalize(pRotation)));
    
    vInObject = mul(float4(vInObject.xyz, 1), orientationMatrix);
    vInObject += float4(pPosition.xyz, 0);

    float4 posInClipSpace = mul(vInObject, ObjectToClipSpace);
    output.pixelPosition = posInClipSpace;

    float2 uv = vertex.TexCoord;
    output.texCoord = float2(uv.x, 1 - uv.y);

    // Pass tangent space basis vectors (for normal mapping).
    float3x3 TBN = float3x3(vertex.Tangent, vertex.Bitangent, vertex.Normal);
    TBN = mul(TBN, (float3x3)orientationMatrix);
    TBN = mul(TBN, (float3x3)ObjectToWorld);

    output.tbnToWorld = float3x3(
        normalize(TBN._m00_m01_m02),
        normalize(TBN._m10_m11_m12),
        normalize(TBN._m20_m21_m22));

    output.worldPosition = mul(vInObject, ObjectToWorld);

    // Fog
    if (FogDistance > 0)
    {
        float4 posInCamera = mul(vInObject, ObjectToCamera);
        float fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
        output.fog = fog;
    }

    return output;
}

float4 psMain(psInput pin) : SV_TARGET
{
    // Sample input textures to get shading model params.
    float4 albedo = BaseColorMap.Sample(texSampler, pin.texCoord) * pin.color;

    if (AlphaCutOff > 0 && albedo.a < AlphaCutOff)
        discard;

    float4 roughnessMetallicOcclusion = RSMOMap.Sample(texSampler, pin.texCoord);
    float roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    float metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    float occlusion = roughnessMetallicOcclusion.z;

    // Outgoing light direction (vector from world-space fragment position to the "eye").
    float3 eyePosition = mul(float4(0, 0, 0, 1), CameraToWorld);
    float3 Lo = normalize(eyePosition - pin.worldPosition);

    // Get current fragment's normal and transform to world space.
    float3 N = normalize(2.0 * NormalMap.Sample(texSampler, pin.texCoord).rgb - 1.0);

    // return float4(pin.tbnToWorld[0],1);
    N = normalize(mul(N, pin.tbnToWorld));

    // Angle between surface normal and outgoing light direction.
    float cosLo = max(0.0, dot(N, Lo));

    // Specular reflection vector.
    float3 Lr = 2.0 * cosLo * N - Lo;

    // Fresnel reflectance at normal incidence (for metals use albedo color).
    float3 F0 = lerp(Fdielectric, albedo, metalness);

    // Direct lighting calculation for analytical lights.
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
        float3 irradiance = PrefilteredSpecular.SampleLevel(texSampler, Lr.xyz, 0.6 * levels).rgb;

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
        // uint specularTextureLevels = querySpecularTextureLevels(BaseColorMap);

        float3 specularIrradiance = PrefilteredSpecular.SampleLevel(texSampler, Lr.xyz, roughness * levels).rgb;
        // float3 specularIrradiance = 0;

        // return float4(specularIrradiance * 1, 1);

        // Split-sum approximation factors for Cook-Torrance specular BRDF.
        float2 specularBRDF = BRDFLookup.SampleLevel(clampedSampler, float2(cosLo, roughness),0).rg;
        // return float4(cosLo, roughness,0,1);

        // Total specular IBL contribution.
        float3 specularIBL = (F0 * specularBRDF.x + specularBRDF.y) * specularIrradiance;

        // Total ambient lighting contribution.
        ambientLighting = diffuseIBL + specularIBL;
    }

    // Final fragment color.

    float4 litColor = float4(directLighting + ambientLighting, 1.0) * BaseColor * Color;
    litColor.rgb = lerp(litColor.rgb, FogColor.rgb, pin.fog);
    litColor += float4(EmissiveColorMap.Sample(texSampler, pin.texCoord).rgb * EmissiveColor.rgb, 0);
    litColor.a *= albedo.a;
    return litColor;
}
