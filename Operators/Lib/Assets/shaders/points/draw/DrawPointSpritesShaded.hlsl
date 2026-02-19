#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/SpriteDef.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"

static const float3 Corners[] =
{
  float3(-0.5, -0.5, 0),
  float3( 0.5, -0.5, 0),
  float3( 0.5,  0.5, 0),
  float3( 0.5,  0.5, 0),
  float3(-0.5,  0.5, 0),
  float3(-0.5, -0.5, 0),
};

static const float4 UV[] =
{
    //    min  max
     //   U V  U V
  float4( 1, 0, 0, 1),
  float4( 0, 0, 1, 1),
  float4( 0, 1, 1, 0),
  float4( 0, 1, 1, 0),
  float4( 1, 1, 0, 0),
  float4( 1, 0, 0, 1),
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
    float Size;
    float AlphaCutOff;
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
    float4 position : SV_POSITION;
    float3 worldPosition : POSITION;
    float3x3 tbnToWorld : TBASIS;    
    float4 color : COLOR;
    float fog:VPOS;
};

sampler texSampler : register(s0);

StructuredBuffer<LegacyPoint> Points : t0;
StructuredBuffer<SpriteDef> Sprites : t1;
Texture2D<float4> FontTexture : register(t2);

Texture2D<float4> BaseColorMap : register(t3);
Texture2D<float4> EmissiveColorMap : register(t4);
Texture2D<float4> RSMOMap : register(t5);
Texture2D<float4> NormalMap : register(t6);

TextureCube<float4> PrefilteredSpecular: register(t7);
Texture2D<float4> BRDFLookup : register(t8);

psInput vsMain(uint id: SV_VertexID)
{
    psInput output;

    int vertexIndex = id % 6;
    int entryIndex = id / 6;

    uint spriteCount, _;
    Sprites.GetDimensions(spriteCount, _);
    uint spriteIndex = entryIndex % spriteCount;

    SpriteDef sprite = Sprites[spriteIndex];

    LegacyPoint p = Points[entryIndex];

    float3 quadCorners = Corners[vertexIndex];
    float3 posInObject =  (-float3(sprite.Pivot, 0) + quadCorners * float3(sprite.Size,0)) * Size * p.Stretch.xyz * p.W;

    float4x4 orientationMatrix = transpose(qToMatrix(p.Rotation));
    posInObject = mul( float4(posInObject.xyz, 1), orientationMatrix);
    posInObject += p.Position;

    float3 normal = normalize(qRotateVec3(float3(0,0,1), p.Rotation));

    // Pass tangent space basis vectors (for normal mapping).
    float3x3 TBN = float3x3(
        normalize(qRotateVec3(float3(1,0,0), p.Rotation)), 
        normalize(qRotateVec3(float3(0,1,0), p.Rotation)), 
        normal 
        );

    TBN = mul(TBN, (float3x3)ObjectToWorld);
    output.tbnToWorld = TBN;

    output.worldPosition =  mul(float4(posInObject,0), ObjectToWorld); 

    float4 pInScreen  = mul(float4(posInObject,1), ObjectToClipSpace);

    float3 lightDirection = float3(1.2, 1, -0.1);
    float phong = pow(  abs(dot(normal,lightDirection )),1);
    
    output.position = pInScreen;

    float4 uv = float4(sprite.UvMin, sprite.UvMax) * UV[vertexIndex];
    output.texCoord =  uv.xy + uv.zw;

    output.color = sprite.Color * Color * p.Color;

    // Fog
    float4 posInCamera = mul(float4(posInObject,1), ObjectToCamera);
    output.fog = pow(saturate(-posInCamera.z/FogDistance), FogBias);
    return output;    
}

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

float4 psMain(psInput psInput) : SV_TARGET
{
    // Font SDF
    float3 smpl1 =  FontTexture.Sample(texSampler, psInput.texCoord).rgb;
    int height, width;
    FontTexture.GetDimensions(width,height);

    float2 dx2 = abs(ddx( psInput.texCoord.xy ) * width);
    float2 dy2 = abs(ddy( psInput.texCoord.xy ) * height);
    float dx= max(dx2.x, dx2.y);
    float dy= max(dy2.x, dy2.y);
    float edge = rsqrt( dx * dx + dy * dy );

    float toPixels = 16 * edge ;
    float sigDist = median( smpl1.r, smpl1.g, smpl1.b ) - 0.5;
    float letterShape = clamp( sigDist * toPixels + 0.5, 0.0, 1.0 );
    if(AlphaCutOff > 0 && letterShape < AlphaCutOff) {
        discard;
    }    
    
    float4 albedo = BaseColorMap.Sample(texSampler, psInput.texCoord).rgba;
    albedo *= letterShape;


    // Sample input textures to get shading model params.
    float4 roughnessSpecularMetallic = RSMOMap.Sample(texSampler, psInput.texCoord);
    float metalness = roughnessSpecularMetallic.z + Metal;
    float normalStrength = roughnessSpecularMetallic.y;
    float roughness = roughnessSpecularMetallic.x + Roughness;

    // Outgoing light direction (vector from world-space fragment position to the "eye").
    float3 eyePosition =  mul( float4(0,0,0,1), CameraToWorld);
    float3 Lo = normalize(eyePosition - psInput.worldPosition);

    // Get current fragment's normal and transform to world space.
    float3 N = lerp(float3(0,0,1),  normalize(2.0 * NormalMap.Sample(texSampler, psInput.texCoord).rgb - 1.0), normalStrength);

    //return float4(psInput.tbnToWorld[0],1);
    N = normalize(mul(N,psInput.tbnToWorld));

    float isFrontSide = dot(N, Lo)/10;
    if( isFrontSide < -0.1)
        N = -N;
    
    // Angle between surface normal and outgoing light direction.
    float cosLo = max(0.0, dot(N, Lo));
        
    // Specular reflection vector.
    float3 Lr = 2.0 * cosLo * N - Lo;

    // Fresnel reflectance at normal incidence (for metals use albedo color).
    float3 F0 = lerp(Fdielectric, albedo, metalness);

    // Direct lighting calculation for analytical lights.
    float3 directLighting = 0.0;
    for(uint i=0; i < ActiveLightCount; ++i)
    {
        float3 Li =   Lights[i].position - psInput.worldPosition; //- Lights[i].direction;
        float distance = length(Li);
        float intensity = Lights[i].intensity / (pow(distance,Lights[i].decay) + 0.2);
        float3 Lradiance = Lights[i].color * intensity; //Lights[i].radiance;

        // Half-vector between Li and Lo.
        float3 Lh = normalize(Li + Lo);

        // Calculate angles between surface normal and various light vectors.
        float cosLi = max(0.0, dot(N, Li));
        float cosLh = max(0.0, dot(N, Lh));

        // Calculate Fresnel term for direct lighting. 
        float3 F  = fresnelSchlick(F0, max(0.0, dot(Lh, Lo)));

        // Calculate normal distribution for specular BRDF.
        float D = ndfGGX(cosLh, roughness);
        // Calculate geometric attenuation for specular BRDF.
        float G = gaSchlickGGX(cosLi, cosLo, roughness);

        // Diffuse scattering happens due to light being refracted multiple times by a dielectric medium.
        // Metals on the other hand either reflect or absorb energy, so diffuse contribution is always zero.
        // To be energy conserving we must scale diffuse BRDF contribution based on Fresnel factor & metalness.
        float3 kd = lerp(float3(1, 1, 1), float3(0, 0, 0), metalness);
        //return float4(F, 1);

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
        //float3 irradiance = 0;// irradianceTexture.Sample(texSampler, N).rgb;
        uint width, height, levels;
        PrefilteredSpecular.GetDimensions(0, width, height, levels);
        float3 irradiance = PrefilteredSpecular.SampleLevel(texSampler, Lr.xyz, 0.8 * levels).rgb;

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
        //uint specularTextureLevels = querySpecularTextureLevels(BaseColorMap);


        float3 specularIrradiance = PrefilteredSpecular.SampleLevel(texSampler, Lr.xyz, roughness * levels).rgb;
        //return float4(specularIrradiance,1);
        //float3 specularIrradiance = 0;

        //return float4(specularIrradiance * 1, 1);

        // Split-sum approximation factors for Cook-Torrance specular BRDF.
        float2 specularBRDF = BRDFLookup.Sample(texSampler, float2(cosLo, roughness)).rg;

        // Total specular IBL contribution.
        float3 specularIBL = (F0 * specularBRDF.x + specularBRDF.y) * specularIrradiance;

        // Total ambient lighting contribution.
        ambientLighting = diffuseIBL + specularIBL;
    }

    // Final fragment color.    

    
    //return float4(directLighting + ambientLighting, 1.0) * BaseColor * Color * float4(1,1,1,albedo.a)
    //     + float4(EmissiveColorMap.Sample(texSampler, psInput.texCoord).rgb * EmissiveColor.rgb, 0);

    // float4 litColor= float4(directLighting + ambientLighting, 1.0) * BaseColor;
    // return lerp(litColor, FogColor, psInput.fog)
    //      + float4(EmissiveColorMap.Sample(texSampler, psInput.texCoord).rgb * EmissiveColor.rgb, 0);    

    float4 litColor= float4(directLighting + ambientLighting, 1.0) * BaseColor * Color;
    litColor.rgb = lerp(litColor.rgb, FogColor.rgb, psInput.fog);
    litColor += float4(EmissiveColorMap.Sample(texSampler, psInput.texCoord).rgb * EmissiveColor.rgb, 0);
    litColor.a *= albedo.a;
    return litColor * psInput.color;
}