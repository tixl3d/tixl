#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"
#include "shared/velocity-functions.hlsl"

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
    float UseFlatShading;
    float SpecularAA;
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

cbuffer FloatParams : register(b5)
{
    /*{FLOAT_PARAMS}*/
}

/** Last frame's transforms of this draw, for the velocity target. Unbound (all 0) in [DrawScene]. */
cbuffer PreviousTransforms : register(b6)
{
    float4x4 PrevCameraToClipSpace;
    float4x4 PrevClipSpaceToCamera;
    float4x4 PrevWorldToCamera;
    float4x4 PrevCameraToWorld;
    float4x4 PrevWorldToClipSpace;
    float4x4 PrevClipSpaceToWorld;
    float4x4 PrevObjectToWorld;
    float4x4 PrevWorldToObject;
    float4x4 PrevObjectToCamera;
    float4x4 PrevObjectToClipSpace;
};

/** Set by [SetShadow] for the draws inside it. Elsewhere all 0, so the transparent ShadowColor skips the lookup. */
cbuffer ShadowParams : register(b7)
{
    float4x4 WorldToLightClipSpace;
    float4 ShadowColor; // Alpha is the strength
    float ShadowBias;
    float ShadowNormalOffset; // In shadow map texels
    float ShadowTexelSizePerW;
    float ShadowSpotCosInner;
    float ShadowSpotCosOuter; // Below -1 when the shadow doesn't come from a spot light
    float ShadowTanHalfFieldOfView;
};

struct psInput
{
    float2 texCoord : TEXCOORD;
    float4 pixelPosition : SV_POSITION;
    float3 worldPosition : POSITION;
    float3x3 tbnToWorld : TBASIS;
    float3 colorRGB : COLOR;
    float fog : VPOS;
    float4 clipPosition : CLIP_POSITION;
    float4 previousClipPosition : PREVIOUS_CLIP_POSITION;
};

struct psOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Velocity : SV_Target2;
    float4 BaseColor : SV_Target3;
    float4 Material : SV_Target4;
};

sampler WrappedSampler : register(s0);
//sampler LinearSampler : register(s1);
sampler ClampedSampler : register(s1);

StructuredBuffer<PbrVertex> PbrVertices : register(t0);
StructuredBuffer<int3> FaceIndices : register(t1);

Texture2D<float4> BaseColorMap : register(t2);
Texture2D<float4> EmissiveColorMap : register(t3);
Texture2D<float4> RSMOMap : register(t4);
Texture2D<float4> NormalMap : register(t5);

TextureCube<float4> PrefilteredSpecular : register(t6);
Texture2D<float4> BRDFLookup : register(t7);
Texture2D<float> ShadowMap : register(t8);

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;

    int faceIndex = id / 3; //  (id % verticesPerInstance) / 3;
    int faceVertexIndex = id % 3;

    PbrVertex vertex = PbrVertices[FaceIndices[faceIndex][faceVertexIndex]];

    float4 posInObject = float4(vertex.Position, 1);

    float4 posInClipSpace = mul(posInObject, ObjectToClipSpace);
    output.pixelPosition = posInClipSpace;
    output.clipPosition = posInClipSpace;
    output.previousClipPosition = mul(posInObject, PrevObjectToClipSpace);

    float2 uv = vertex.TexCoord;
    output.texCoord = float2(uv.x, 1 - uv.y);
    
    output.colorRGB = vertex.ColorRGB;

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

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Additional Resources ==========================================
/*{RESOURCES(t9)}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------

//-------------------------------------------------------------------
inline float4 GetField(float4 p)
{
#ifndef USE_WORLDSPACE
    //p.xyz = mul(float4(p.xyz, 1), WorldToObject).xyz;
#endif
    float4 f = 1;
    /*{FIELD_CALL}*/

    return f;
}

float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}
//===================================================================

/** 1 where the light reaches the surface, 0 in shadow, filtered over 3x3 shadow map texels. */
float ComputeShadowLight(float3 worldPosition, float3 normal)
{
    // A texel covers more of the surface further from a perspective light, so the offset grows with the distance (w).
    float texelWorldSize = mul(float4(worldPosition, 1), WorldToLightClipSpace).w * ShadowTexelSizePerW;
    float4 posInLightClipSpace = mul(float4(worldPosition + normal * ShadowNormalOffset * texelWorldSize, 1), WorldToLightClipSpace);
    float3 shadowCoord = posInLightClipSpace.xyz / posInLightClipSpace.w;
    float2 offsetFromAxis = shadowCoord.xy * ShadowTanHalfFieldOfView;
    shadowCoord.xy = shadowCoord.xy * float2(0.5, -0.5) + 0.5;

    // Outside the shadow map nothing is known about occluders.
    if (any(shadowCoord.xy < 0) || any(shadowCoord.xy > 1) || shadowCoord.z > 1)
        return 1;

    uint width, height;
    ShadowMap.GetDimensions(width, height);
    float2 texelSize = 1.0 / float2(width, height);

    float shadowedCount = 0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float occluderDepth = ShadowMap.SampleLevel(ClampedSampler, shadowCoord.xy + float2(x, y) * texelSize, 0);
            shadowedCount += shadowCoord.z > occluderDepth + ShadowBias ? 1 : 0;
        }
    }

    // Outside a spot light's cone its light doesn't reach the surface, so its shadow doesn't either. Same fade as
    // EvaluateLight(), with the angle taken from the shadow camera, which looks along the cone's axis.
    float cosAngleToAxis = rsqrt(1 + dot(offsetFromAxis, offsetFromAxis));
    float cone = smoothstep(ShadowSpotCosOuter, max(ShadowSpotCosInner, ShadowSpotCosOuter + 1e-4), cosAngleToAxis);
    return 1 - shadowedCount / 9 * cone;
}

float3 ApplyShadow(float3 litColor)
{
    if (ShadowColor.a <= 0)
        return litColor;

    float light = ComputeShadowLight(frag.worldPosition, frag.N);
    return lerp(litColor, ShadowColor.rgb, (1 - light) * ShadowColor.a);
}

#define PBR_APPLY_SHADOW(litColor) ApplyShadow(litColor)
#include "shared/pbr-render.hlsl"

/** Set by psMain. Back faces of meshes drawn without culling need their normal flipped to face the viewer. */
static bool IsFrontFacing = true;

float3 ComputeNormal(psInput pin, float3x3 tbnToWorld)
{
    float3 N;
    if (UseFlatShading > 0.5)
    {
        // Flat shading: calculate geometric normal from world position derivatives
        float3 dpdx = ddx(pin.worldPosition);
        float3 dpdy = ddy(pin.worldPosition);
        float3 geometricNormal = normalize(cross(dpdy, dpdx));

        // Apply normal map details on top of flat normal
        float4 normalMap = NormalMap.Sample(WrappedSampler, pin.texCoord);
        float3 normalDetail = normalize(2.0 * normalMap.rgb - 1.0);

        // Create TBN basis using geometric normal and derivatives
        float3 T = normalize(dpdx);
        float3 B = normalize(cross(geometricNormal, T));
        T = cross(B, geometricNormal); // Reorthogonalize
        float3x3 flatTBN = float3x3(T, B, geometricNormal);

        // Apply normal map in flat shading tangent space
        N = normalize(mul(normalDetail, flatTBN));
    }
    else
    {
        // Standard shading: use interpolated normals with normal mapping
        float4 normalMap = NormalMap.Sample(WrappedSampler, pin.texCoord);
        N = normalize(2.0 * normalMap.rgb - 1.0);
        N = normalize(mul(N, tbnToWorld));

        // The flat shading branch derives its normal from screen-space derivatives, which already faces the viewer.
        if (!IsFrontFacing)
            N = -N;
    }
    return N;
}

inline float AdjustRoughnessForSpecularAA(float baseRoughness)
{
 // --- Specular anti-aliasing ---
    // Compute normal variance using screen-space derivatives and increase roughness accordingly.
    // This reduces specular aliasing on silhouettes and high-frequency normalmap regions.
    float3 Nx = ddx(frag.N);
    float3 Ny = ddy(frag.N);
    float normalVar = max(0.0, max(dot(Nx, Nx), dot(Ny, Ny)));
    normalVar *= SpecularAA;
    // convert roughness -> alpha (energy-preserving), combine variance, then convert back
    float baseR = saturate(baseRoughness);
    float baseR2 = baseR * baseR;
    float adjustedR = sqrt(baseR2 + normalVar);    
    return saturate(adjustedR);
}

psOutput psMain(psInput pin, bool isFrontFace : SV_IsFrontFace)
{
    IsFrontFacing = isFrontFace;
    psOutput output;
    
    float4 roughnessMetallicOcclusion = RSMOMap.Sample(WrappedSampler, pin.texCoord);

    
    frag.Metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    frag.Occlusion = roughnessMetallicOcclusion.z;
    frag.albedo = BaseColorMap.Sample(WrappedSampler, pin.texCoord);
    frag.albedo.rgb *= pin.colorRGB;
    frag.uv = pin.texCoord;
    frag.N = ComputeNormal(pin, pin.tbnToWorld);

    frag.Roughness = AdjustRoughnessForSpecularAA(roughnessMetallicOcclusion.x + Roughness, SpecularAA);

    frag.fog = pin.fog;
    frag.worldPosition = pin.worldPosition;

    float4 eyePosition = mul(float4(0, 0, 0, 1), CameraToWorld);
    frag.Lo = normalize(eyePosition.xyz - frag.worldPosition);

    float4 litColor = ComputePbr();

    float4 fieldColor = GetField(float4(pin.worldPosition.xyz, 0));
    litColor.rgba *= fieldColor;
    
    // Alpha testing
    if (AlphaCutOff > 0 && litColor.a < AlphaCutOff)
    {
        discard;
    }
    
    float3 worldNormal = frag.N;
    // Output to color buffer (SV_Target0)
    output.Color = litColor;
    // Output to normal buffer (SV_Target1)
    output.Normal = float4(worldNormal, 1.0);
    output.Velocity = EncodeVelocity(pin.clipPosition, pin.previousClipPosition, CameraToClipSpace, PrevCameraToClipSpace);
    GetMaterialTargets(fieldColor.rgb, 1, output.BaseColor, output.Material);
    
    return output;
}
