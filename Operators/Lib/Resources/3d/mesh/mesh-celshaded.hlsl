//This shader is a hack of TiXL's DrawMeshHatched, it can certainly be improved.

#include "shared/point.hlsl"
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
    float4 Color;
    float4 EdgeColor;
    
    float Brightness;
    
    float Edgethreshold;
    float RequestedResolutionHeight;
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

struct psInput
{
    float2 texCoord : TEXCOORD;
    float4 pixelPosition : SV_POSITION;
    float3 worldPosition : POSITION;
    float3x3 tbnToWorld : TBASIS;    
    float fog:VPOS;
    int faceid: FACEID;
    
};

sampler texSampler : register(s0);

StructuredBuffer<PbrVertex> PbrVertices : t0;
StructuredBuffer<int3> FaceIndices : t1;

Texture2D<float4> BaseColorMap : register(t2);
Texture2D<float4> ShadingGradient : register(t3);

psInput vsMain(uint id: SV_VertexID)
{
    psInput output;

    int faceIndex = id / 3;//  (id % verticesPerInstance) / 3;
    int faceVertexIndex = id % 3;

    PbrVertex vertex = PbrVertices[FaceIndices[faceIndex][faceVertexIndex]];

    float4 posInObject = float4( vertex.Position,1);

    float4 posInClipSpace = mul(posInObject, ObjectToClipSpace);
    output.pixelPosition = posInClipSpace;

    float2 uv = vertex.TexCoord;
    output.texCoord = float2(uv.x , 1- uv.y);

    // Pass tangent space basis vectors (for normal mapping).
    float3x3 TBN = float3x3(vertex.Tangent, vertex.Bitangent, vertex.Normal);
    TBN = mul(TBN, (float3x3)ObjectToWorld);
    output.tbnToWorld = TBN;

    output.worldPosition =  mul(posInObject, ObjectToWorld); 
   
    // Fog
    if(FogDistance > 0) 
    {
        float4 posInCamera = mul(posInObject, ObjectToCamera);
        float fog = pow(saturate(-posInCamera.z/FogDistance), FogBias);
        output.fog = fog;
    }

   


    output.faceid = faceIndex;
    
    return output;
}

//
// based on https://github.com/Nadrin/PBR/blob/master/data/shaders/hlsl/pbr.hlsl

float4 psMain(psInput pin) : SV_TARGET
{
    // Sample input textures to get shading model params.
    float4 albedo = BaseColorMap.Sample(texSampler, pin.texCoord);
    //float4 roughnessSpecularMetallic = RSMOMap.Sample(texSampler, pin.texCoord);
    float metalness = 0;//roughnessSpecularMetallic.z + Metal;
    //float normalStrength = roughnessSpecularMetallic.y;
    float roughness = 1;//roughnessSpecularMetallic.x + Roughness;

    // Outgoing light direction (vector from world-space fragment position to the "eye").
    float3 eyePosition =  mul( float4(0,0,0,1), CameraToWorld);
    float3 Lo = normalize(eyePosition - pin.worldPosition);

    // Get current fragment's normal and transform to world space.
   
    //return float4(pin.tbnToWorld[0],1);
    float3 Norg = normalize(mul(float3(0,0,1),pin.tbnToWorld));
     
    float3 randFaceOffset = hash31((float)pin.faceid);// * RandomFaceOffset;
    float3 N = Norg;// + randFaceOffset;
    
    
    // Angle between surface normal and outgoing light direction.
    float cosLo = max(0.0, dot(N, Lo));
        
    // Specular reflection vector.
    float3 Lr = 8.0 * cosLo * N - Lo;

    // Fresnel reflectance at normal incidence (for metals use albedo color).
    float3 F0 = lerp(Fdielectric, albedo, metalness);
   
    // Direct lighting calculation for analytical lights.
    float3 directLighting = 0.0;
    for(uint i=0; i < ActiveLightCount; ++i)
    {
        float3 Li =   Lights[i].position - pin.worldPosition; //- Lights[i].direction;
        float distance = length(Li);
        float intensity = Lights[i].intensity / (pow(distance/Lights[i].range, Lights[i].decay) + 1);
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
        float3 diffuseBRDF = kd * albedo.rgb ;

        // Cook-Torrance specular microfacet BRDF.
        float3 specularBRDF = ((F * D * G) / max(Epsilon, 4.0 * cosLi * cosLo)) * 1;

        // Total contribution for this light.
        directLighting += (diffuseBRDF + specularBRDF) * Lradiance * cosLi ;
        //directLighting = cosLi;
    }


    // Rotate
    float3 p = pin.pixelPosition.xyz;// + float3(0.5, -0.5, 0);
    
    //float brightness = (directLighting.r + directLighting.g+ directLighting.b)/3 + randFaceOffset * RandomFaceLighting;
    float brightness = (directLighting.r + directLighting.g+ directLighting.b)*Brightness;
    brightness = lerp(brightness , (FogColor.r + FogColor.g + FogColor.b)/3, pin.fog);

    float4 shadingOffset = ShadingGradient.Sample(texSampler, float2(brightness, 0))*Color*albedo;

    float normalShade = saturate(dot(Norg, Lo )); // this is how I got the edge shading, I'm not 100% convinced but it looks okay
    float4 celShaded;
    // we dra
    if (normalShade>Edgethreshold){
        celShaded = shadingOffset;
    }
    else{
        celShaded = EdgeColor;
    }
        
    return celShaded;
    
    
}
