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

/** Last frame's transforms of this draw, for the velocity target. */
cbuffer PreviousTransforms : register(b1) // b5:1
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

cbuffer Params : register(b2) // 1:2
{
    float4x4 WorldToLightClipSpace;
    float4 Color;

    float Size;
    float AlphaCutOff;
    float ShadowOffset;
    float ShadowBias;

    float4 ShadowColor;
};

cbuffer IntParams : register(b3) //2:3
{
    int SegmentCount;
    int UseWForSize;
    int UseStretch;
    int IsShadowPass;
};

cbuffer FogParams : register(b4) //3:4
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

cbuffer PointLights : register(b5) //4:5
{
    PointLight Lights[8];
    int ActiveLightCount;
}

cbuffer PbrParams : register(b6) // 6:6
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
    float4 color : COLOR;
    float3 worldPosition : POSITION;
    float3x3 tbnToWorld : TBASIS;
    float fog : VPOS;
    float4 positionInLightClipSpace : LIGHTCLIPPOS;
    float4 clipPosition : CLIP_POSITION;
    float4 previousClipPosition : PREVIOUS_CLIP_POSITION;
};




sampler WrappedSampler : register(s0);
sampler ClampedSampler : register(s1);

// t2 and t10 are unused: each point is its own sub-draw of an indexed multi-draw indirect command, and the
// hardware reads the chunk's faces from the bound index buffer.

Texture2D<float4> BaseColorMap : register(t0); // 3-0
Texture2D<float4> EmissiveColorMap : register(t1); // 4:1
Texture2D<float4> RSMOMap : register(t2); // 5:2
Texture2D<float4> NormalMap : register(t3); // 6:3
Texture2D<float4> BRDFLookup : register(t4); // 8:4
StructuredBuffer<Point> Points : register(t5); //1:5
StructuredBuffer<Point> PreviousPoints : register(t6); //11:7
StructuredBuffer<PbrVertex> PbrVertices : register(t7); // 0:6
/** The points one frame earlier, in the same order. Unbound or shorter, points fall back to their current position. */
TextureCube<float4> PrefilteredSpecular : register(t8); //7:8

Texture2D<float> ShadowMap : register(t9); // 9:9



// Each sub-draw walks its chunk's index range through the bound index buffer and carries its point index as
// firstInstance. With a zero vertex offset, SV_VertexID is the absolute index.
psInput vsMain(uint vertexIndex : SV_VertexID, uint firstInstance : SV_StartInstanceLocation)
{
    psInput output;

    uint pointIndex = firstInstance;

    PbrVertex vertex = PbrVertices[vertexIndex];
    float4 posInObject = float4(vertex.Position, 1);

    float resizeFromW = UseWForSize ? Points[pointIndex].FX1 : 1;
    float3 resizeFromStretch = UseStretch ? Points[pointIndex].Scale : 1;
    posInObject.xyz *= max(0, resizeFromW) * Size * resizeFromStretch;
    float3 vertexAroundPoint = posInObject.xyz;
    float4x4 orientationMatrix = transpose(qToMatrix(normalize(Points[pointIndex].Rotation)));
    posInObject = mul(float4(posInObject.xyz, 1), orientationMatrix);

    posInObject += float4(Points[pointIndex].Position, 0);

    // An unbound or shorter buffer reads zeros; a zero rotation marks such a point.
    uint previousCount, previousStride;
    PreviousPoints.GetDimensions(previousCount, previousStride);
    Point previousPoint = Points[pointIndex];
    if ((uint)pointIndex < previousCount)
    {
        Point candidate = PreviousPoints[pointIndex];
        if (dot(candidate.Rotation, candidate.Rotation) > 1e-6)
            previousPoint = candidate;
    }

    // The same vertex placed at last frame's point: the chunk moves and turns with it.
    float4x4 previousOrientationMatrix = transpose(qToMatrix(normalize(previousPoint.Rotation)));
    float4 previousPosInObject = mul(float4(vertexAroundPoint, 1), previousOrientationMatrix) + float4(previousPoint.Position, 0);
    output.previousClipPosition = PreviousClipUnlessJumped(mul(previousPosInObject, PrevObjectToClipSpace),
                                                           mul(float4(Points[pointIndex].Position, 1), ObjectToClipSpace),
                                                           mul(float4(previousPoint.Position, 1), PrevObjectToClipSpace));
    output.color = Points[pointIndex].Color * float4(vertex.ColorRGB, 1);

    float4 posInClipSpace = mul(posInObject, ObjectToClipSpace);
    output.pixelPosition = posInClipSpace;
    output.clipPosition = posInClipSpace;

    float2 uv = vertex.TexCoord;
    output.texCoord = float2(uv.x, 1 - uv.y);

    // Offset position along normal for shadow mapping
    float4 posInWorld = mul(posInObject, ObjectToWorld);
    output.worldPosition = posInWorld.xyz;

    float3 worldNormal = normalize(mul(float4(vertex.Normal, 0), (float4x4)ObjectToWorld).xyz);
    float4 posInWorldOffset = posInWorld;
    posInWorldOffset.xyz += worldNormal * ShadowOffset;
    output.positionInLightClipSpace = mul(posInWorldOffset, WorldToLightClipSpace);

    // Pass tangent space basis vectors (for normal mapping).
    float3x3 TBN = float3x3(vertex.Tangent, vertex.Bitangent, vertex.Normal);
    TBN = mul(TBN, (float3x3)orientationMatrix);
    TBN = mul(TBN, (float3x3)ObjectToWorld);

    output.tbnToWorld = float3x3(
        normalize(TBN._m00_m01_m02),
        normalize(TBN._m10_m11_m12),
        normalize(TBN._m20_m21_m22));

    // Fog
    output.fog = 0;
    if (FogDistance > 0)
    {
        float4 posInCamera = mul(posInObject, ObjectToCamera);
        float fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
        output.fog = fog;
    }

    return output;
}

inline float ComputeShadowFactor(float3 shadowCoord, float2 texelSize, float bias)
{
    float shadow = 0.0;
    int samples = 0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2(x, y) * texelSize;
            float2 sampleUV = shadowCoord.xy + offset;

            // Sample only if sampleUV is within [0,1]
            if (sampleUV.x >= 0 && sampleUV.x <= 1 && sampleUV.y >= 0 && sampleUV.y <= 1)
            {
                float sampleDepth = ShadowMap.Sample(ClampedSampler, sampleUV);

                // Compare depths with bias
                if (shadowCoord.z > sampleDepth + bias)
                {
                    shadow += 1.0;
                }
            }
            samples++;
        }
    }
    return 1.0 - (shadow / samples);
}

#include "shared/pbr-render.hlsl"

/** 1 where the shadow map leaves the surface lit. Without a [SetShadow] upstream the slot holds a 1x1 placeholder and nothing is shadowed. */
float ComputeShadow(float4 positionInLightClipSpace)
{
    if (IsShadowPass)
        return 1;

    uint shadowMapWidth, shadowMapHeight;
    ShadowMap.GetDimensions(shadowMapWidth, shadowMapHeight);
    if (shadowMapWidth <= 1)
        return 1;

    float3 shadowCoord = positionInLightClipSpace.xyz / positionInLightClipSpace.w;
    shadowCoord.xy = shadowCoord.xy * 0.5 + 0.5;
    shadowCoord.y = 1 - shadowCoord.y;
    if (shadowCoord.x < 0 || shadowCoord.x > 1 || shadowCoord.y < 0 || shadowCoord.y > 1)
        return 1;

    float2 shadowMapTexelSize = float2(1.0 / shadowMapWidth, 1.0 / shadowMapHeight);
    return ComputeShadowFactor(shadowCoord, shadowMapTexelSize, ShadowBias);
}

struct psOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Velocity : SV_Target2;
    float4 BaseColor : SV_Target3;
    float4 Material : SV_Target4;
};

psOutput psMain(psInput pin, bool isFrontFace : SV_IsFrontFace)
{
    frag.albedo = BaseColorMap.Sample(WrappedSampler, pin.texCoord) * pin.color;
    if (AlphaCutOff > 0 && frag.albedo.a < AlphaCutOff)
        discard;

    float4 roughnessMetallicOcclusion = RSMOMap.Sample(WrappedSampler, pin.texCoord);
    frag.Roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    frag.Metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    frag.Occlusion = roughnessMetallicOcclusion.z;
    frag.uv = pin.texCoord;
    frag.fog = pin.fog;
    frag.worldPosition = pin.worldPosition;

    float3 N = normalize(2.0 * NormalMap.Sample(WrappedSampler, pin.texCoord).rgb - 1.0);
    N = normalize(mul(N, pin.tbnToWorld));

    // Back faces of geometry drawn without culling need the normal facing the viewer.
    if (!isFrontFace)
        N = -N;

    frag.N = N;

    float3 eyePosition = mul(float4(0, 0, 0, 1), CameraToWorld).xyz;
    frag.Lo = normalize(eyePosition - frag.worldPosition);

    float4 litColor = ComputePbr();
    float shadow = ComputeShadow(pin.positionInLightClipSpace);
    litColor.rgb = lerp(litColor.rgb, ShadowColor.rgb, (1 - shadow) * ShadowColor.a);

    psOutput output;
    output.Color = litColor;
    output.Normal = float4(N, 1);
    output.Velocity = EncodeVelocity(pin.clipPosition, pin.previousClipPosition, CameraToClipSpace, PrevCameraToClipSpace);
    GetMaterialTargets(1, 1, output.BaseColor, output.Material);
    return output;
}
