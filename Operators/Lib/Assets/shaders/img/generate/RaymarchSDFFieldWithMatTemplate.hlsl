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

    float SpecularAA;
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
// static sampler LinearSampler = TexSampler;
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
    // ToDo: Should be done only if required
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
    float4 color : SV_Target0;
    float4 Normal : SV_Target1;
    float depth : SV_Depth;
};

float ComputeDepthFromViewZ(float viewZ)
{
    float4 clipPos = mul(float4(0, 0, viewZ, 1), CameraToClipSpace);
    return clipPos.z / clipPos.w;
}

// Unpack from [0,1] to [-1,1]
float3 UnpackNormal(float4 packedNormal)
{
    float3 normal = packedNormal.rgb * 2.0 - 1.0;
    return normalize(normal);
}

#include "shared/pbr-render.hlsl"

static const float3 w1 = float3(+1, -1, -1);
static const float3 w2 = float3(-1, +1, -1);
static const float3 w3 = float3(-1, -1, +1);
static const float3 w4 = float3(+1, +1, +1);

struct TriPlanarN
{
    float3 nWorld;
    float3 weights; // for debugging
    float2 uv;
    float3 fieldPos;
};

// This should probably be optimized
TriPlanarN TriplanarNormal2(float3 p, float h, float scale, Texture2D texN, SamplerState samp)
{
    // Center (for local coords q)
    float4 c = GetField(float4(p, 0));

    // Tetrahedral samples (same pattern as your normal)
    float4 s1 = GetField(float4(p + h * w1, 0));
    float4 s2 = GetField(float4(p + h * w2, 0));
    float4 s3 = GetField(float4(p + h * w3, 0));
    float4 s4 = GetField(float4(p + h * w4, 0));

    // World normal from SDF gradient
    float3 Nw = normalize(s1.w * w1 + s2.w * w2 + s3.w * w3 + s4.w * w4);

    // Jacobian rows J = ∂q/∂p
    float3 gu = s1.x * w1 + s2.x * w2 + s3.x * w3 + s4.x * w4;
    float3 gv = s1.y * w1 + s2.y * w2 + s3.y * w3 + s4.y * w4;
    float3 gw = s1.z * w1 + s2.z * w2 + s3.z * w3 + s4.z * w4;

    // Inverse-Jacobian columns (world dirs of local +X,+Y,+Z)
    float3 c0 = cross(gv, gw);
    float3 c1 = cross(gw, gu);
    float3 c2 = cross(gu, gv);
    float det = dot(gu, c0) + 1e-20;
    float invDet = 1.0 / det;

    float3 Xw = normalize(c0 * invDet);
    float3 Yw = normalize(c1 * invDet);
    float3 Zw = normalize(c2 * invDet);

    // Object-space weights (soft, with tiny overlap to avoid pure-face collapse)
    float3 aN_obj = abs(float3(dot(gu, Nw), dot(gv, Nw), dot(gw, Nw)));
    float sharpness = 0.3;
    float3 wN = pow(aN_obj, sharpness);
    wN = max(wN, float3(1e-4, 1e-4, 1e-4));
    wN /= (wN.x + wN.y + wN.z);

    // UVs from local coords q
    float3 q = c.xyz;
    float2 uvX = q.zy / scale; // (U,V) = (Z,Y)
    float2 uvY = q.xz / scale; // (U,V) = (X,Z)
    float2 uvZ = q.xy / scale; // (U,V) = (X,Y)

    // Primary face
    uint face = (wN.x > wN.y) ? ((wN.x > wN.z) ? 0u : 2u)
                              : ((wN.y > wN.z) ? 1u : 2u);

    // Primary UV
    float2 uvPrimary = (face == 0u) ? uvX : (face == 1u) ? uvY
                                                         : uvZ;

    // --- Robust per-face frames (axis-facing safe) ---
    const float EPS = 1e-6;

    // Base U/V world axes that match UVs
    float3 Ux0 = Zw, Vx0 = Yw; // X face: U=Z, V=Y
    float3 Uy0 = Xw, Vy0 = Zw; // Y face: U=X, V=Z
    float3 Uz0 = Xw, Vz0 = Yw; // Z face: U=X, V=Y

    // Project to tangent plane
    float3 Tx_p = Ux0 - Nw * dot(Nw, Ux0);
    float3 Bx_p = Vx0 - Nw * dot(Nw, Vx0);
    float lx_u = dot(Tx_p, Tx_p);
    float lx_v = dot(Bx_p, Bx_p);

    float3 Ty_p = Uy0 - Nw * dot(Nw, Uy0);
    float3 By_p = Vy0 - Nw * dot(Nw, Vy0);
    float ly_u = dot(Ty_p, Ty_p);
    float ly_v = dot(By_p, By_p);

    float3 Tz_p = Uz0 - Nw * dot(Nw, Uz0);
    float3 Bz_p = Vz0 - Nw * dot(Nw, Vz0);
    float lz_u = dot(Tz_p, Tz_p);
    float lz_v = dot(Bz_p, Bz_p);

    // Face X
    float3 Tx, Bx;
    if (lx_u < EPS && lx_v < EPS)
    {
        Tx = normalize(cross((abs(Nw.z) < 0.9) ? float3(0, 0, 1) : float3(0, 1, 0), Nw));
        Bx = normalize(cross(Nw, Tx));
    }
    else if (lx_u < EPS)
    {
        Bx = normalize(Bx_p);
        Tx = normalize(cross(Bx, Nw));
    }
    else if (lx_v < EPS)
    {
        Tx = normalize(Tx_p);
        Bx = normalize(cross(Nw, Tx));
    }
    else
    {
        Tx = normalize(Tx_p);
        Bx = normalize(Bx_p);
    }

    // Face Y
    float3 Ty, By;
    if (ly_u < EPS && ly_v < EPS)
    {
        Ty = normalize(cross((abs(Nw.z) < 0.9) ? float3(0, 0, 1) : float3(1, 0, 0), Nw));
        By = normalize(cross(Nw, Ty));
    }
    else if (ly_u < EPS)
    {
        By = normalize(By_p);
        Ty = normalize(cross(By, Nw));
    }
    else if (ly_v < EPS)
    {
        Ty = normalize(Ty_p);
        By = normalize(cross(Nw, Ty));
    }
    else
    {
        Ty = normalize(Ty_p);
        By = normalize(By_p);
    }

    // Face Z
    float3 Tz, Bz;
    if (lz_u < EPS && lz_v < EPS)
    {
        Tz = normalize(cross((abs(Nw.y) < 0.9) ? float3(0, 1, 0) : float3(1, 0, 0), Nw));
        Bz = normalize(cross(Nw, Tz));
    }
    else if (lz_u < EPS)
    {
        Bz = normalize(Bz_p);
        Tz = normalize(cross(Bz, Nw));
    }
    else if (lz_v < EPS)
    {
        Tz = normalize(Tz_p);
        Bz = normalize(cross(Nw, Tz));
    }
    else
    {
        Tz = normalize(Tz_p);
        Bz = normalize(Bz_p);
    }

    // Handedness check (should be +1 after cross, but keep for safety)
    float hx = (dot(cross(Tx, Bx), Nw) >= 0.0) ? 1.0 : -1.0;
    if (hx < 0.0)
        Bx = -Bx;
    float hy = (dot(cross(Ty, By), Nw) >= 0.0) ? 1.0 : -1.0;
    if (hy < 0.0)
        By = -By;
    float hz = (dot(cross(Tz, Bz), Nw) >= 0.0) ? 1.0 : -1.0;
    if (hz < 0.0)
        Bz = -Bz;

    // Sample normals; flip green only if the face ended up left-handed
    float3 nX = UnpackNormal(texN.Sample(samp, uvX + 0.5));
    nX.y *= -hx;
    float3 nY = UnpackNormal(texN.Sample(samp, uvY + 0.5));
    nY.y *= -hy;
    float3 nZ = UnpackNormal(texN.Sample(samp, uvZ + 0.5));
    nZ.y *= -hz;

    // Rows = T,B,N (left-multiply), then blend by weights
    float3x3 TBNx = float3x3(Tx, Bx, Nw);
    float3x3 TBNy = float3x3(Ty, By, Nw);
    float3x3 TBNz = float3x3(Tz, Bz, Nw);

    float3 wNX = mul(nX, TBNx);
    float3 wNY = mul(nY, TBNy);
    float3 wNZ = mul(nZ, TBNz);

    float3 nWorld = normalize(wNX * wN.x + wNY * wN.y + wNZ * wN.z);

    TriPlanarN outv;
    outv.nWorld = nWorld;
    outv.weights = wN;
    outv.uv = uvPrimary;
    outv.fieldPos = c.xyz;
    return outv;
}

PSOutput psMain(vsOutput input)
{
    float3 eye = input.worldTViewPos;

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

    // We've got a hit or we're not sure.
    if (D < MAX_DIST)
    {
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

    // PBR shading -------------------------------------------------------------------------
    TriPlanarN r = TriplanarNormal2(p, NormalSamplingDistance, TextureScale, NormalMap, WrappedSampler);
    float3 normal = r.nWorld;
    float3 fieldPos = r.fieldPos;
    float3 absN = abs(normal);

#if MAPPING_GLOBAL_TRIPLANAR
    float2 uv = (absN.x > absN.y && absN.x > absN.z) ? p.yz / TextureScale : (absN.y > absN.z) ? p.zx / TextureScale
                                                                                               : p.xy / TextureScale;
#elif MAPPING_LOCAL_TRIPLANAR
    float2 uv = r.uv;

#elif MAPPING_XY
    float2 uv = fieldPos.xy / TextureScale;
#elif MAPPING_XZ
    float2 uv = fieldPos.xz / TextureScale;
#else
    float2 uv = fieldPos.yz / TextureScale;
#endif

    float4 DEBUG_RESULT = float4(normal, 1);
    float4 fieldColor = float4(GetField(float4(p, 1)).rgb, 1);

    uv += 0.5;
    float4 roughnessMetallicOcclusion = RSMOMap.Sample(WrappedSampler, uv);

    //frag.Roughness = SpecularAA;
    //frag.Roughness = saturate(roughnessMetallicOcclusion.x + Roughness);

    frag.Metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    frag.Occlusion = roughnessMetallicOcclusion.z;
    frag.albedo = BaseColorMap.Sample(WrappedSampler, uv);
    frag.uv = uv;
    frag.N = normal;
    frag.Lo = -dp;
    frag.worldPosition = mul(float4(p, 1), ObjectToWorld);

    frag.Roughness = AdjustRoughnessForSpecularAA(roughnessMetallicOcclusion.x + Roughness, SpecularAA);
    
    float4 litColor = ComputePbr();
    litColor *= fieldColor;

    // Fog
    float depth = dot(eye - p, -input.viewDir);
    float fog = FogDistance <= 0 ? 0 : pow(saturate(depth / FogDistance), FogBias);

    litColor.rgb = lerp(litColor.rgb * fieldColor.rgb, FogColor.rgb, fog * FogColor.a);

    // litColor += float4(EmissiveColorMap.Sample(WrappedSampler, uv).rgb * EmissiveColor.rgb, 0);
    // litColor.a *= frag.albedo.a;

    litColor.rgb = lerp(AmbientOcclusion.rgb, litColor.rgb, ComputeAO(p, normal, AODistance, 3, AmbientOcclusion.a * (1 - fog)));

    PSOutput result;
    result.color = clamp(litColor, 0, float4(1000, 1000, 1000, 1));
    // result.color = DEBUG_RESULT * float4(0, 0, 1, 1);

    float viewZ = mul(float4(p, 1), WorldToCamera).z;
    result.depth = ComputeDepthFromViewZ(viewZ);
    result.Normal = float4(normal, 1.0);
    return result;
}
