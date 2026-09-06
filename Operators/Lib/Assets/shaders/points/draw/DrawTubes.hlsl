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
    float WidthFX;
    float UseScale;
    float UniformScale;
    float CapStart;
    float CapEnd;
    float Smooth;
    float RoundAmount;
    float SubSegCount;
    float DistanceFade;
    float FadeStartDist;
    float FadeEndDist;
    float MinWidthFactor;
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

cbuffer Transforms : register(b5)
{
    int SideCount;
};



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

StructuredBuffer<Point> Points : register(t0);
//Texture2D<float4> texture2 : register(t1);

Texture2D<float4> BaseColorMap : register(t1);
Texture2D<float4> EmissiveColorMap : register(t2);
Texture2D<float4> RSMOMap : register(t3);
Texture2D<float4> NormalMap : register(t4);
TextureCube<float4> PrefilteredSpecular: register(t5);


Texture2D<float4> BRDFLookup : register(t6);

static const int DrawsPerQuad =6;
static int DrawsPerStep = DrawsPerQuad * SideCount;
static const float Tau = 3.141578 * 2;

// Redistributes spacing along the line around each point (independent scale mode only).
// A point with Scale.x > 1 pulls its neighbors apart along the line, < 1 pushes them together.
float3 LineStretch(int i, uint pointCount)
{
    if (pointCount < 2 || UseScale < 0.5 || UniformScale >= 0.5) return float3(0, 0, 0);

    float3 pos = Points[i].Position;
    float3 disp = 0;

    if (i > 0)
    {
        float s = Points[i - 1].Scale.x;
        s = isnan(s) ? 1 : s;
        disp += (s - 1.0) * 0.5 * (pos - Points[i - 1].Position);
    }

    if (i < (int)pointCount - 1)
    {
        float s = Points[i + 1].Scale.x;
        s = isnan(s) ? 1 : s;
        disp += (s - 1.0) * 0.5 * (pos - Points[i + 1].Position);
    }

    return disp;
}

float3 EffectivePos(int i, uint pointCount)
{
    return Points[i].Position + LineStretch(i, pointCount);
}

float3 getTangent(int i, uint pointCount)
{
    if (pointCount < 2) return float3(1,0,0);
    if (i == 0)          return EffectivePos(1, pointCount) - EffectivePos(0, pointCount);
    if (i == (int)pointCount - 1) return EffectivePos(i, pointCount) - EffectivePos(i-1, pointCount);
    return (EffectivePos(i+1, pointCount) - EffectivePos(i-1, pointCount)) * 0.5;
}

float3 hermitePos(float3 p0, float3 m0, float3 p1, float3 m1, float t)
{
    float t2 = t*t, t3 = t2*t;
    return (2*t3-3*t2+1)*p0 + (t3-2*t2+t)*m0 + (-2*t3+3*t2)*p1 + (t3-t2)*m1;
}

psInput vsMain(uint id: SV_VertexID)
{
    uint pointCount, pointStride;
    Points.GetDimensions(pointCount, pointStride);

    psInput output;
    int subSegs = (Smooth >= 0.5) ? (int)SubSegCount : 1;
    uint tubeVerts = (pointCount - 1) * (uint)subSegs * DrawsPerStep;
    uint capVertsPerCap = (SideCount + 1) * 3;
    uint capVerts = capVertsPerCap * 2;

    if (id < tubeVerts)
    {
        int indexInLineStep = id % DrawsPerStep;
        int cornerIndex = indexInLineStep % DrawsPerQuad;
        int sideIndex = indexInLineStep / DrawsPerQuad;

        int subSegId = id / DrawsPerStep;
        int sourceSeg = subSegId / subSegs;
        int subIndex = subSegId % subSegs;

float3 cornerFactors = Corners[cornerIndex];
        float t = (cornerFactors.x < 0.5 ? (float)subIndex : (float)(subIndex + 1)) / (float)subSegs;
        float f = ((float)sourceSeg + t) / clamp((float)(pointCount - 1), 1.0, 100000.0);

        Point p0 = Points[sourceSeg];
        Point p1 = Points[sourceSeg + 1];

        bool separator = IsSeparator(p0) || IsSeparator(p1);

        float3 scale0 = p0.Scale;
        scale0.x = isnan(scale0.x) ? 1 : scale0.x;
        scale0.y = isnan(scale0.y) ? 1 : scale0.y;
        scale0.z = isnan(scale0.z) ? 1 : scale0.z;
        float3 scale1 = p1.Scale;
        scale1.x = isnan(scale1.x) ? 1 : scale1.x;
        scale1.y = isnan(scale1.y) ? 1 : scale1.y;
        scale1.z = isnan(scale1.z) ? 1 : scale1.z;
        float3 pointScale = lerp(scale0, scale1, t);

        bool independentScale = (UseScale >= 0.5 && UniformScale < 0.5);

float3 pos0 = EffectivePos(sourceSeg, pointCount);
        float3 pos1 = EffectivePos(sourceSeg + 1, pointCount);
        float3 pPos;
        if (Smooth >= 0.5)
        {
            float3 m0 = getTangent(sourceSeg, pointCount);
            float3 m1 = getTangent(sourceSeg + 1, pointCount);
            pPos = hermitePos(pos0, m0, pos1, m1, t);
        }
        else
        {
            pPos = lerp(pos0, pos1, t);
        }

        float4 q0 = p0.Rotation;
        float4 q1 = p1.Rotation;
        if (dot(q0, q1) < 0) q1 = -q1;
        float4 pointRotation = normalize(lerp(q0, q1, t));

        float w0, w1;
        if (UseScale >= 0.5 && UniformScale >= 0.5)
        {
            w0 = scale0.x;
            w1 = scale1.x;
        }
        else if (WidthFX >= 0.5 && WidthFX < 1.5)
        {
            w0 = isnan(p0.FX1) ? 1 : p0.FX1;
            w1 = isnan(p1.FX1) ? 1 : p1.FX1;
        }
        else if (WidthFX >= 1.5)
        {
            w0 = isnan(p0.FX2) ? 1 : p0.FX2;
            w1 = isnan(p1.FX2) ? 1 : p1.FX2;
        }
        else
        {
            w0 = 1; w1 = 1;
        }
        float WidthFactor = lerp(w0, w1, t);
        if (independentScale)
        {
            WidthFactor = 1.0; // Cross-section is scaled per-axis below
        }
        if (separator)
        {
            WidthFactor = NAN; // Collapse the segment at separators
        }
        WidthFactor *= (1.0 + RoundAmount * sin(3.14159265 * t));

        float distFactor = 1.0;
        if (DistanceFade >= 0.5)
        {
            float4 centerCamPos = mul(float4(pPos, 1), ObjectToCamera);
            float camDist = -centerCamPos.z;
            float distT = saturate((camDist - FadeStartDist) / max(FadeEndDist - FadeStartDist, 0.001));
            distFactor = lerp(1.0, MinWidthFactor, distT);
        }

        float fRing = (sideIndex + (cornerFactors.y / 2 + 0.5)) / SideCount;
        float angleOffset = (Spin + Twist * f) * 3.14159265 / 180.0;
        float spinRad = fRing * Tau + angleOffset;

        float sy = independentScale ? pointScale.y : 1.0;
        float sz = independentScale ? pointScale.z : 1.0;

        float3 side = float3(0, cos(spinRad) * sy, sin(spinRad) * sz);
        float3 radiusOffset = qRotateVec3(side, pointRotation) * Width * WidthFactor * distFactor;

        float3 pInObject = pPos + radiusOffset;

        output.texCoord = float2( f * (TextureRange.y - TextureRange.x) + TextureRange.x,
        fRing);

        float3 tangent = normalize(qRotateVec3(float3(1,0,0), pointRotation));
        float3 normalLocal = normalize(float3(0, cos(spinRad) / max(sy, Epsilon), sin(spinRad) / max(sz, Epsilon)));
        float3 normal = normalize(qRotateVec3(normalLocal, pointRotation));
        float3 bitangent = normalize(cross(tangent, normal));
        float3x3 TBN = float3x3(
            tangent,
            bitangent,
            normal
            );
        TBN = mul(TBN, (float3x3)ObjectToWorld);
        output.tbnToWorld = TBN;

        output.worldPosition =  mul(float4(pInObject,1), ObjectToWorld);
        output.pixelPosition = mul(float4(pInObject,1), ObjectToClipSpace);

        float4 posInCamera = mul(float4(pInObject,1), ObjectToCamera);
        output.fog = pow(saturate(-posInCamera.z/FogDistance), FogBias);
        output.color = Color * lerp(p0.Color, p1.Color, t);
    }
    else
    {
        uint capId = id - tubeVerts;
        uint capIndex = capId / capVertsPerCap;
        uint triLocalId = capId % capVertsPerCap;
        uint triInCap = triLocalId / 3;
        uint vertInTri = triLocalId % 3;

        Point p;
        float3 capNormal;
        bool capEnabled;

        // The point rotation's X axis is the line tangent: outward for the end cap, inward for the start cap.
        if (capIndex == 1)
        {
            p = Points[pointCount - 1];
            capNormal = normalize(qRotateVec3(float3(1, 0, 0), p.Rotation));
            capEnabled = CapEnd >= 0.5;
        }
        else
        {
            p = Points[0];
            capNormal = -normalize(qRotateVec3(float3(1, 0, 0), p.Rotation));
            capEnabled = CapStart >= 0.5;
        }

        bool separator = IsSeparator(p);

        float WidthFactor;
        if (UseScale >= 0.5 && UniformScale >= 0.5)
        {
            WidthFactor = isnan(p.Scale.x) ? 1 : p.Scale.x;
        }
        else if (WidthFX >= 0.5 && WidthFX < 1.5)
        {
            WidthFactor = isnan(p.FX1) ? 1 : p.FX1;
        }
        else if (WidthFX >= 1.5)
        {
            WidthFactor = isnan(p.FX2) ? 1 : p.FX2;
        }
        else
        {
            WidthFactor = 1;
        }
        float sy = 1.0, sz = 1.0;
        if (UseScale >= 0.5 && UniformScale < 0.5)
        {
            WidthFactor = 1; // Cross-section is scaled per-axis below
            sy = isnan(p.Scale.y) ? 1 : p.Scale.y;
            sz = isnan(p.Scale.z) ? 1 : p.Scale.z;
        }
        if (separator)
        {
            WidthFactor = NAN; // Collapse the cap at separators
        }
        float radius = Width * WidthFactor;
        int capPointIndex = (capIndex == 1) ? (int)pointCount - 1 : 0;
        float3 capCenter = EffectivePos(capPointIndex, pointCount);
        if (DistanceFade >= 0.5)
        {
            float4 capCamPos = mul(float4(capCenter, 1), ObjectToCamera);
            float camDist = -capCamPos.z;
            float distT = saturate((camDist - FadeStartDist) / max(FadeEndDist - FadeStartDist, 0.001));
            radius *= lerp(1.0, MinWidthFactor, distT);
        }

        uint rimIndex;
        if (vertInTri == 0)
        {
            rimIndex = 0xFFFFFFFF;
        }
        else if (vertInTri == 1)
        {
            // The start cap's winding is flipped so its front faces outward, matching its normal.
            rimIndex = (capIndex == 0) ? triInCap + 1 : triInCap;
        }
        else
        {
            rimIndex = (capIndex == 0) ? triInCap : triInCap + 1;
        }

        float3 pInObject;
        float3 tangent;
        float3 normal;
        float3 bitangent;
        float2 texCoord;

        if (!capEnabled)
        {
            pInObject = capCenter;
            tangent = normalize(qRotateVec3(float3(0, 1, 0), p.Rotation));
            normal = float3(0, 0, 0);
            bitangent = normalize(cross(normal, tangent));
            texCoord = float2(0, 0);
        }
        else if (rimIndex == 0xFFFFFFFF)
        {
            pInObject = separator ? float3(NAN, NAN, NAN) : capCenter;
            tangent = normalize(qRotateVec3(float3(0, 1, 0), p.Rotation));
            normal = capNormal;
            bitangent = normalize(cross(normal, tangent));
            texCoord = float2(0.5, 0.5);
        }
        else
        {
            float capF = (capIndex == 1) ? 1.0 : 0.0;
            float capAngleOffset = (Spin + Twist * capF) * 3.14159265 / 180.0;
            float colF = (float)(rimIndex % (SideCount + 1));
            float angle = colF / SideCount * Tau + capAngleOffset;
            float3 dir = float3(0, cos(angle) * sy, sin(angle) * sz);
            pInObject = capCenter + qRotateVec3(dir, p.Rotation) * radius;

            tangent = normalize(qRotateVec3(float3(0, -sin(angle), cos(angle)), p.Rotation));
            normal = capNormal;
            bitangent = normalize(cross(normal, tangent));
            texCoord = float2(cos(angle), sin(angle)) * 0.5 + 0.5;
        }

        output.texCoord = texCoord;

        float3x3 TBN = float3x3(
            tangent,
            bitangent,
            normal
            );
        TBN = mul(TBN, (float3x3)ObjectToWorld);
        output.tbnToWorld = TBN;

        output.worldPosition = mul(float4(pInObject, 1), ObjectToWorld);
        output.pixelPosition = mul(float4(pInObject, 1), ObjectToClipSpace);

        float4 posInCamera = mul(float4(pInObject, 1), ObjectToCamera);
        output.fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
        output.color = Color * p.Color;
    }

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
    float3 eyePosition =  mul( float4(0,0,0,1), CameraToWorld);
    float3 Lo = normalize(eyePosition - pin.worldPosition);

    // Get current fragment's normal and transform to world space.
    float3 N = normalize(2.0 * NormalMap.Sample(texSampler, pin.texCoord).rgb - 1.0);


    //return float4(pin.tbnToWorld[0],1);
    N = normalize(mul(N,pin.tbnToWorld));
    //return float4(N.xyz,1);


    float cosLo = abs(dot(N, Lo));
        
    // Specular reflection vector.
    float3 Lr = 2.0 * cosLo * N - Lo;

    // Fresnel reflectance at normal incidence (for metals use albedo color).
    float3 F0 = lerp(Fdielectric, albedo, metalness);

    // Direct lighting calculation for analytical lights.
    float3 directLighting = 0.0;
    for(uint i=0; i < ActiveLightCount; ++i)
    {
    float3 Lvec = Lights[i].position - pin.worldPosition;
    float distance = length(Lvec);
    float3 L = Lvec / max(distance, 1e-4);
    float intensity = Lights[i].intensity / (pow(distance/Lights[i].range, Lights[i].decay) + 1);
    float3 Lradiance = Lights[i].color * intensity;

    float3 Lh = normalize(L + Lo);
    float cosLi = max(0.0, dot(N, L));
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
        float3 kd = lerp(1.0 - F, 0.0, metalness);
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
        //uint specularTextureLevels = querySpecularTextureLevels(BaseColorMap);


        float3 specularIrradiance = PrefilteredSpecular.SampleLevel(texSampler, Lr.xyz, roughness * levels).rgb;
        //float3 specularIrradiance = 0;

        //return float4(specularIrradiance * 1, 1);

        // Split-sum approximation factors for Cook-Torrance specular BRDF.
        float2 specularBRDF = BRDFLookup.SampleLevel(clampedSampler, float2(cosLo, roughness), 0).rg;
        //return float4(cosLo, roughness,0,1);

        // Total specular IBL contribution.
        float3 specularIBL = (F0 * specularBRDF.x + specularBRDF.y) * specularIrradiance;

        // Total ambient lighting contribution.
        ambientLighting = (diffuseIBL + specularIBL) * occlusion;
    }

    // Final fragment color.    

    
    //return float4(directLighting + ambientLighting, 1.0) * BaseColor * Color * float4(1,1,1,albedo.a)
    //     + float4(EmissiveColorMap.Sample(texSampler, pin.texCoord).rgb * EmissiveColor.rgb, 0);

    // float4 litColor= float4(directLighting + ambientLighting, 1.0) * BaseColor;
    // return lerp(litColor, FogColor, pin.fog)
    //      + float4(EmissiveColorMap.Sample(texSampler, pin.texCoord).rgb * EmissiveColor.rgb, 0);    

    float4 litColor= float4(directLighting + ambientLighting, 1.0) * BaseColor * pin.color;
    litColor.rgb = lerp(litColor.rgb, FogColor.rgb, pin.fog * FogColor.a);
    litColor += float4(EmissiveColorMap.Sample(texSampler, pin.texCoord).rgb * EmissiveColor.rgb, 0);
    litColor.a *= albedo.a;
    return litColor;
}