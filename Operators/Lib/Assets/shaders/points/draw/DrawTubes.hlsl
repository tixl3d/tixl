#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/point-light.hlsl"
#include "shared/pbr.hlsl"
#include "shared/velocity-functions.hlsl"

/** x selects the segment's start or end ring, y the side of the quad between two ring vertices. */
static const float2 Corners[] =
{
  float2(0, -1),
  float2(1, -1),
  float2(1,  1),
  float2(1,  1),
  float2(0,  1),
  float2(0, -1),
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
    float WidthFX;
    float UsePointScale;
    float CapInset;
    float FlatAround;
    float FlatAlong;
    float Miter;
    float2 TextureRange;
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

cbuffer IntParams : register(b5)
{
    int SideCount;
    int TextureMode;
    int CloseEnds;
};

/** Last frame's transforms of this draw, for the velocity target. */
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

struct psInput
{
    float2 texCoord : TEXCOORD;
    float4 pixelPosition : SV_POSITION;
    float3 worldPosition : POSITION;
    float3x3 tbnToWorld : TBASIS;
    float fog : VPOS;
    float4 color : COLOR;
    float4 clipPosition : CLIPPOSITION;
    float4 previousClipPosition : PREVIOUSCLIPPOSITION;
};

sampler texSampler : register(s0);
sampler clampedSampler : register(s1);

StructuredBuffer<Point> Points : register(t0);

Texture2D<float4> BaseColorMap : register(t1);
Texture2D<float4> EmissiveColorMap : register(t2);
Texture2D<float4> RSMOMap : register(t3);
Texture2D<float4> NormalMap : register(t4);
TextureCube<float4> PrefilteredSpecular : register(t5);
Texture2D<float4> BRDFLookup : register(t6);

// Registers are unique across both stages: Vulkan derives a slot's descriptor type from its declaration, so a
// buffer and a texture sharing one would bind the wrong type.


static const int DrawsPerQuad = 6;
static const float Tau = 3.14159265 * 2;

/** Sharper corners than this keep a narrower joint instead of throwing out long spikes, like SVG's miter limit. */
static const float MaxMiterStretch = 4;

#include "points/draw/DrawTubesCenterline.hlsl"

/** Per point, the ring orientation and distances from the rotation-minimizing frame pre-pass. VS-only. */
StructuredBuffer<RingFrame> RingFrames : register(t7);

/** The points one frame earlier, in the same order. Unbound or shorter, points count as not moving. VS-only. */
StructuredBuffer<Point> PreviousPoints : register(t8);

/** Position of a point along the tube as chosen by TextureMode, before TextureRange: drives both the texture and
    the twist, so a twisting texture stays aligned with its stripes. */
float GetCoordinateAlongTube(int pointIndex, uint pointCount)
{
    // The continuous modes let the distance run on through all lines of the buffer instead of restarting per line.
    RingFrame frame = RingFrames[pointIndex];
    bool isContinuous = TextureMode >= 5;
    float distance = isContinuous ? frame.Line.y : frame.NormalAndDistance.w;
    float totalDistance = isContinuous ? frame.Line.z : frame.Line.x;
    bool isNormalized = TextureMode == 4 || TextureMode == 6;

    // Distance is in world units, so points added at a line's end leave the texture before them in place.
    return TextureMode == 1   ? Points[pointIndex].FX1
           : TextureMode == 2 ? Points[pointIndex].FX2
           : TextureMode >= 3 ? (isNormalized ? distance / max(totalDistance, Epsilon) : distance)
                              : (float)pointIndex / max((float)pointCount - 1, 1);
}

/** How far a point moved since the last frame, or zero without a matching previous point. */
float3 GetPointMovement(int pointIndex, uint pointCount)
{
    uint previousCount, previousStride;
    PreviousPoints.GetDimensions(previousCount, previousStride);
    if (previousCount != pointCount)
        return 0;

    // An unbound buffer reads zeros; a zero rotation marks such a point.
    Point previousPoint = PreviousPoints[pointIndex];
    if (dot(previousPoint.Rotation, previousPoint.Rotation) < 1e-6)
        return 0;

    return Points[pointIndex].Position - previousPoint.Position;
}

psInput vsMain(uint id : SV_VertexID)
{
    uint pointCount, pointStride;
    Points.GetDimensions(pointCount, pointStride);

    psInput output;

    int drawsPerSegment = DrawsPerQuad * SideCount;
    int segmentIndex = id / drawsPerSegment;
    int indexInSegment = id % drawsPerSegment;
    int sideIndex = indexInSegment / DrawsPerQuad;
    float2 corner = Corners[indexInSegment % DrawsPerQuad];

    // Each ring sits at a point and is shared by the segments on both sides of it, so joints stay closed.
    int pointIndex = segmentIndex + (int)corner.x;
    Point linePoint = Points[pointIndex];

    // The draw covers one segment less than there are points; a separator at either end collapses the segment.
    bool hasSegment = segmentIndex + 1 < (int)pointCount;
    if (hasSegment)
        hasSegment = IsLinePoint(segmentIndex, pointCount) && IsLinePoint(segmentIndex + 1, pointCount);

    // Closed ends turn the segment at each line end into a cone: its end ring collapses into a tip on the axis,
    // which CapInset moves towards the neighbour ring until the cone flattens into a disc.
    int ringIndex = pointIndex;
    int tipIndex = pointIndex;
    bool isCapSegment = false;
    bool isTip = false;
    float3 tipPosition = 0;
    float3 capAxis = 0;
    float capLength = 0;
    if (CloseEnds != 0 && hasSegment)
    {
        bool startIsLineEnd = !IsLinePoint(segmentIndex - 1, pointCount);
        bool endIsLineEnd = !IsLinePoint(segmentIndex + 2, pointCount);

        // A line of two points has no inner ring to close towards, so it stays open.
        if (startIsLineEnd != endIsLineEnd)
        {
            tipIndex = startIsLineEnd ? segmentIndex : segmentIndex + 1;
            ringIndex = startIsLineEnd ? segmentIndex + 1 : segmentIndex;
            float3 basePosition = Points[ringIndex].Position;
            float3 endPosition = Points[tipIndex].Position;
            tipPosition = lerp(endPosition, basePosition, CapInset);
            capAxis = SafeNormalize(endPosition - basePosition, float3(1, 0, 0));
            capLength = length(tipPosition - basePosition);
            isCapSegment = true;
            isTip = pointIndex == tipIndex;
        }
    }

    // A cap segment takes its frame, width and spin from the ring it closes, so its rim meets that ring exactly.
    Point ringPoint = Points[ringIndex];
    float3 direction = CenterlineTangent(ringIndex, pointCount);
    float3 ringNormal = RingFrames[ringIndex].NormalAndDistance.xyz;
    ringNormal = SafeNormalize(ringNormal - dot(ringNormal, direction) * direction, AnyPerpendicular(direction));

    // This handedness makes the outside of the tube its front face.
    float3 ringBinormal = cross(ringNormal, direction);

    float widthFactor = WidthFX < 0.5   ? 1
                        : WidthFX < 1.5 ? ringPoint.FX1
                                        : ringPoint.FX2;
    float2 crossSectionScale = UsePointScale > 0.5 ? ringPoint.Scale.yz : 1;
    if (!hasSegment)
        widthFactor = NAN;

    float ringFraction = (sideIndex + (corner.y * 0.5 + 0.5)) / SideCount;
    float angleOffset = (Spin + Twist * GetCoordinateAlongTube(ringIndex, pointCount)) * 3.14159265 / 180.0;
    float angle = ringFraction * Tau + angleOffset;
    float cosAngle = cos(angle);
    float sinAngle = sin(angle);

    // FlatAround turns both edges of a side towards the normal of its center, so at 1 each side reads as one facet.
    float sideCenterAngle = (sideIndex + 0.5) / SideCount * Tau + angleOffset;
    float normalAngle = lerp(angle, sideCenterAngle, FlatAround);

    float3 side = ringNormal * (cosAngle * crossSectionScale.x) + ringBinormal * (sinAngle * crossSectionScale.y);

    // A round ring in the bisector plane pinches the corner; stretching it along the bend by 1 / cos(half the bend
    // angle) keeps both segments at their full width.
    float3 miterAxis = 0;
    float miterStretch = 1;
    float3 outgoing = DirectionToDistinctNeighbour(ringIndex, 1, pointCount);
    float3 incoming = DirectionToDistinctNeighbour(ringIndex, -1, pointCount);
    if (dot(outgoing, outgoing) > 0.5 && dot(incoming, incoming) > 0.5)
    {
        float cosHalfBend = dot(direction, outgoing);
        float3 bend = outgoing - cosHalfBend * direction;
        if (dot(bend, bend) > 1e-8)
        {
            miterAxis = normalize(bend);
            float fullStretch = min(1 / max(cosHalfBend, 1 / MaxMiterStretch), MaxMiterStretch);
            miterStretch = max(lerp(1, fullStretch, Miter), 0.01);
        }
    }
    side += miterAxis * (dot(side, miterAxis) * (miterStretch - 1));
    float3 radius = side * Width * widthFactor;
    float3 pInObject = isTip ? tipPosition : ringPoint.Position + radius;

    // The normal of an elliptic cross-section leans towards its narrow axis.
    float3 normal = SafeNormalize(ringNormal * (cos(normalAngle) / max(crossSectionScale.x, Epsilon))
                                  + ringBinormal * (sin(normalAngle) / max(crossSectionScale.y, Epsilon)), side);

    // On a mitered joint the average of both segments' normals is the stretched ring's own normal: inverse stretch.
    normal = SafeNormalize(normal + miterAxis * (dot(normal, miterAxis) * (1 / miterStretch - 1)), normal);

    // FlatAlong turns the normal from across the bend at the point to across this segment, so at 1 each segment
    // shades like a straight cylinder piece. A cone's surface tilts towards its axis by its radius over its length.
    if (isCapSegment)
    {
        normal = SafeNormalize(normal * capLength + capAxis * length(radius), capAxis);
    }
    else
    {
        int nextIndex = min(segmentIndex + 1, (int)pointCount - 1);
        float3 segmentDirection = SafeNormalize(Points[nextIndex].Position - Points[segmentIndex].Position, direction);
        float3 acrossSegment = normal - dot(normal, segmentDirection) * segmentDirection;
        normal = SafeNormalize(lerp(normal, acrossSegment, FlatAlong), normal);
    }

    float u = GetCoordinateAlongTube(pointIndex, pointCount);
    output.texCoord = float2(lerp(TextureRange.x, TextureRange.y, u), ringFraction);

    float3x3 TBN = float3x3(direction, normalize(cross(direction, normal)), normal);
    output.tbnToWorld = mul(TBN, (float3x3)ObjectToWorld);

    output.worldPosition = mul(float4(pInObject, 1), ObjectToWorld).xyz;
    output.pixelPosition = mul(float4(pInObject, 1), ObjectToClipSpace);

    // Rings move with their point; a cap's tip moves like the point it sits between. Ring rotation and width changes
    // are left out, as for point sprites.
    float3 ringMovement = GetPointMovement(ringIndex, pointCount);
    float3 movement = isTip ? lerp(GetPointMovement(tipIndex, pointCount), ringMovement, CapInset) : ringMovement;
    float4 previousClipPosition = mul(float4(pInObject - movement, 1), PrevObjectToClipSpace);
    float4 centerClipPosition = mul(float4(ringPoint.Position, 1), ObjectToClipSpace);
    float4 previousCenterClipPosition = mul(float4(ringPoint.Position - ringMovement, 1), PrevObjectToClipSpace);
    output.clipPosition = output.pixelPosition;
    output.previousClipPosition = PreviousClipUnlessJumped(previousClipPosition, centerClipPosition, previousCenterClipPosition);

    float4 posInCamera = mul(float4(pInObject, 1), ObjectToCamera);
    output.fog = pow(saturate(-posInCamera.z / FogDistance), FogBias);
    output.color = Color * linePoint.Color;
    return output;
}

struct psOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Velocity : SV_Target2;
};

psOutput psMain(psInput pin, bool isFrontFace : SV_IsFrontFace)
{
    // Sample input textures to get shading model params.
    float4 albedo = BaseColorMap.Sample(texSampler, pin.texCoord).rgba;
    float4 roughnessMetallicOcclusion = RSMOMap.Sample(texSampler, pin.texCoord);
    float roughness = saturate(roughnessMetallicOcclusion.x + Roughness);
    float metalness = saturate(roughnessMetallicOcclusion.y + Metal);
    float occlusion = roughnessMetallicOcclusion.z;

    // Outgoing light direction (vector from world-space fragment position to the "eye").
    float3 eyePosition =  mul( float4(0,0,0,1), CameraToWorld).xyz;
    float3 Lo = normalize(eyePosition - pin.worldPosition);

    // Get current fragment's normal and transform to world space.
    float3 N = normalize(2.0 * NormalMap.Sample(texSampler, pin.texCoord).rgb - 1.0);


    //return float4(pin.tbnToWorld[0],1);
    N = normalize(mul(N,pin.tbnToWorld));

    // Back faces of geometry drawn without culling need the normal facing the viewer.
    if (!isFrontFace)
        N = -N;
    //return float4(N.xyz,1);


    float cosLo = abs(dot(N, Lo));
        
    // Specular reflection vector.
    float3 Lr = 2.0 * cosLo * N - Lo;

    // Fresnel reflectance at normal incidence (for metals use albedo color).
    float3 F0 = lerp(Fdielectric, albedo.rgb, metalness);

    // Direct lighting calculation for analytical lights.
    float3 directLighting = 0.0;
    for(uint i=0; i < (uint)ActiveLightCount; ++i)
    {
    float3 Lvec = Lights[i].position - pin.worldPosition;
    float distance = length(Lvec);
    float3 L = Lvec / max(distance, 1e-4);
    float intensity = Lights[i].intensity / (pow(distance/Lights[i].range, Lights[i].decay) + 1);
    float3 Lradiance = Lights[i].color.rgb * intensity;

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
    psOutput output;
    output.Color = litColor;
    output.Normal = float4(N, 1);
    output.Velocity = EncodeVelocity(pin.clipPosition, pin.previousClipPosition, CameraToClipSpace, PrevCameraToClipSpace);
    return output;
}