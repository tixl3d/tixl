#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer ParamConstants : register(b1)
{
    float MaxSteps;
    float StepSize;
    float MinDistance;
    float MaxDistance;

    float4 Color;
    float4 AmbientOcclusion;

    float TextureScale;
    float AODistance;
    float NormalSamplingDistance;
    float DistToColor;
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
    float Specular;
    float Metal;
}

Texture2D<float4> BaseColorMap : register(t0);
Texture2D<float4> EmissiveColorMap : register(t1);
Texture2D<float4> RSMOMap : register(t2);
Texture2D<float4> NormalMap : register(t3);
Texture2D<float4> BRDFLookup : register(t4);
TextureCube<float4> PrefilteredSpecular : register(t5);

sampler ClampedSampler : register(s0);
sampler WrappedSampler : register(s1);
//static sampler LinearSampler = TexSampler;
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

//=== Additional Resources ==========================================
/*{RESOURCES(t6)}*/

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------
float4 GetField(float4 p)
{
    p.xyz = mul(float4(p.xyz, 1), WorldToObject).xyz;
    float4 f = 1;
    /*{FIELD_CALL}*/

    float uniformScale = length(ObjectToWorld[0].xyz);
    f.w *= uniformScale;
    return f;
}

float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}
//===================================================================

float3 GetNormal(float3 p, float offset)
{
    return normalize(
        GetDistance(p + float3(offset, -offset, -offset)) * float3(1, -1, -1) +
        GetDistance(p + float3(-offset, offset, -offset)) * float3(-1, 1, -1) +
        GetDistance(p + float3(-offset, -offset, offset)) * float3(-1, -1, 1) +
        GetDistance(p + float3(offset, offset, offset)) * float3(1, 1, 1));
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
    float4 clipPos = mul(float4(0, 0, viewZ, 1), CameraToClipSpace);
    return clipPos.z / clipPos.w;
}

float3 UnpackNormal(float4 packedNormal)
{
    // Unpack from [0,1] to [-1,1]
    float3 normal = packedNormal.rgb * 2.0 - 1.0;
    return normalize(normal);
}

#include "shared/pbr-render.hlsl"

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
    for (steps = 0; steps < maxSteps && abs(D) > MinDistance && D < MaxDistance; steps++)
    {
        D = GetDistance(p) * StepSize;
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
        normal = normalize(GetNormal(p, NormalSamplingDistance));

        // col = Color.rgb;
        //  We've gone through all steps, but we haven't hit anything.
        //  Mix in the background color.
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

    float4 f = float4(GetField(float4(p, 0)).rgb, 1);
    float3 fieldPos = f.xyz;

    // PBR shading -------------------------------------------------------------------------

    // Tri-planar mappping
    float3 absN = abs(normal);

#if MAPPING_GLOBAL_TRIPLANAR
    float2 uv = (absN.x > absN.y && absN.x > absN.z) ? p.yz / TextureScale : (absN.y > absN.z) ? p.zx / TextureScale
                                                                                               : p.xy / TextureScale;
#elif MAPPING_LOCAL_TRIPLANAR
    float2 uv = (absN.x > absN.y && absN.x > absN.z) ? fieldPos.yz / TextureScale : (absN.y > absN.z) ? fieldPos.zx / TextureScale
                                                                                                     : fieldPos.xy / TextureScale;

#elif MAPPING_XY
    float2 uv = fieldPos.xy / TextureScale;
#elif MAPPING_XZ
    float2 uv = fieldPos.xz / TextureScale;
#else
    float2 uv = fieldPos.yz / TextureScale;
#endif

    // Sample and unpack normal map
    float4 normalMapSample = NormalMap.Sample(WrappedSampler, uv);
    float3 tangentNormal = UnpackNormal(normalMapSample);

    // Create tangent space matrix
    float3 N = normalize(normal);  // Original geometric normal
    float3 T = normalize(cross(N, float3(0, 0, 1)));  // Tangent
    float3 B = normalize(cross(N, T));  // Bitangent
    float3x3 TBN = float3x3(T, B, N);

    // Transform normal from tangent space to world space
    float3 finalNormal = normalize(mul(tangentNormal, TBN));

    // Use the final normal in all subsequent calculations
    normal = finalNormal;

    float4 fieldColor = float4(GetField(float4(p, 1)).rgb, 1);

    float4 roughnessMetallicOcclusion = RSMOMap.Sample(WrappedSampler, uv);
    frag.Roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    frag.Metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    frag.Occlusion = roughnessMetallicOcclusion.z;
    frag.albedo = BaseColorMap.Sample(WrappedSampler, uv);
    frag.uv = uv;
    frag.N = normal;
    frag.Lo = -dp;
    frag.worldPosition = mul(float4(p, 1), ObjectToWorld);

    float4 litColor = ComputePbr();
    litColor *= fieldColor;

    // Fog
    float depth = dot(eye - p, -input.viewDir);
    float fog = FogDistance <= 0 ? 0 : pow(saturate(depth / FogDistance), FogBias);

    litColor.rgb = lerp(litColor.rgb * fieldColor.rgb, FogColor.rgb, fog * FogColor.a);

    // litColor.rgb *= fieldColor.rgb;

    litColor += float4(EmissiveColorMap.Sample(WrappedSampler, uv).rgb * EmissiveColor.rgb, 0);
    litColor.a *= frag.albedo.a;

    litColor.rgb = lerp(AmbientOcclusion.rgb, litColor.rgb, ComputeAO(p, normal, AODistance, 3, AmbientOcclusion.a * (1 - fog)));

    PSOutput result;
    result.color = clamp(litColor, 0, float4(1000, 1000, 1000, 1));

    float viewZ = mul(float4(p, 1), WorldToCamera).z;
    result.depth = ComputeDepthFromViewZ(viewZ);
    return result;
}

