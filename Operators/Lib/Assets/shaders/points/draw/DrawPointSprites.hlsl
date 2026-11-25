#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/SpriteDef.hlsl"
#include "shared/velocity-functions.hlsl"

static const float3 Quad[] =
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
    float ScaleFactor;
    float UsePointScale;
    float AlphaCutOff;
};

/** Last frame's transforms of this draw, for the velocity target. */
cbuffer PreviousTransforms : register(b2)
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
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float4 color : COLOR;
    float4 clipPosition : CLIP_POSITION;
    float4 previousClipPosition : PREVIOUS_CLIP_POSITION;
};

struct psOutput
{
    float4 Color : SV_Target0;

    // Sprites have no surface normal. Zero marks that, and with alpha 0 blended sprites keep the normal behind them.
    float4 Normal : SV_Target1;
    float4 Velocity : SV_Target2;
};

StructuredBuffer<Point> Points : register(t0);
StructuredBuffer<SpriteDef> Sprites : register(t1);
Texture2D<float4> fontTexture : register(t2);

/** The points one frame earlier, in the same order. Unbound or shorter, points fall back to their current position. */
StructuredBuffer<Point> PreviousPoints : register(t3);
sampler texSampler : register(s0);

float3 GetCornerInObject(Point p, SpriteDef sprite, float3 quadCorner)
{
    float sizeFxFactor = ScaleFactor == 0
                             ? 1
                         : (ScaleFactor == 1) ? p.FX1
                                              : p.FX2;

    float3 s = Size * sizeFxFactor * (UsePointScale ? p.Scale : 1);
    float3 posInObject = (-float3(sprite.Pivot, 0) + quadCorner * float3(sprite.Size, 0)) * s;

    float4x4 orientationMatrix = transpose(qToMatrix(p.Rotation));
    posInObject = mul(float4(posInObject.xyz, 1), orientationMatrix).xyz;
    return posInObject + p.Position;
}


psInput vsMain(uint id: SV_VertexID)
{
    psInput output;

    int vertexIndex = id % 6;
    int entryIndex = id / 6;

    uint spriteCount, _;
    Sprites.GetDimensions(spriteCount, _);
    uint spriteIndex = entryIndex % spriteCount;

    SpriteDef sprite = Sprites[spriteIndex];

    Point p = Points[entryIndex];

    float3 quadCorner = Quad[vertexIndex];
    float3 posInObject = GetCornerInObject(p, sprite, quadCorner);

    float4 quadPosInWorld = mul(float4(posInObject.xyz,1), ObjectToWorld);
    float4 quadPosInCamera = mul(quadPosInWorld, WorldToCamera);
    output.position = mul(quadPosInCamera, CameraToClipSpace);
    output.clipPosition = output.position;

    // An unbound or shorter buffer reads zeros; a zero rotation marks such a point.
    uint previousCount, previousStride;
    PreviousPoints.GetDimensions(previousCount, previousStride);
    Point previousPoint = p;
    if ((uint)entryIndex < previousCount)
    {
        Point candidate = PreviousPoints[entryIndex];
        if (dot(candidate.Rotation, candidate.Rotation) > 1e-6)
            previousPoint = candidate;
    }

    float3 previousPosInObject = GetCornerInObject(previousPoint, sprite, quadCorner);
    float4 previousQuadInClip = mul(mul(mul(float4(previousPosInObject, 1), PrevObjectToWorld), PrevWorldToCamera),
                                    PrevCameraToClipSpace);
    float4 centerInClip = mul(mul(mul(float4(p.Position, 1), ObjectToWorld), WorldToCamera), CameraToClipSpace);
    float4 previousCenterInClip = mul(mul(mul(float4(previousPoint.Position, 1), PrevObjectToWorld), PrevWorldToCamera),
                                      PrevCameraToClipSpace);
    output.previousClipPosition = PreviousClipUnlessJumped(previousQuadInClip, centerInClip, previousCenterInClip);

    float4 uv = float4(sprite.UvMin, sprite.UvMax) * UV[vertexIndex];
    output.texCoord =  uv.xy + uv.zw;
  
    output.color =  Color * p.Color;
    return output;
}


float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

psOutput psMain(psInput input)
{
    float3 smpl1 =  fontTexture.Sample(texSampler, input.texCoord).rgb;
    int height, width;
    fontTexture.GetDimensions(width,height);

    float2 dx2 = abs(ddx( input.texCoord.xy ) * width);
    float2 dy2 = abs(ddy( input.texCoord.xy ) * height);
    float dx= max(dx2.x, dx2.y);
    float dy= max(dy2.x, dy2.y);
    float edge = rsqrt( dx * dx + dy * dy );

    float toPixels = 16 * edge ;
    float sigDist = median( smpl1.r, smpl1.g, smpl1.b ) - 0.5;
    float letterShape = clamp( sigDist * toPixels + 0.5, 0.0, 1.0 );
    
    if(AlphaCutOff > 0 && letterShape * input.color.a < AlphaCutOff) {
        discard;
    }
    
    psOutput output;
    output.Color = float4(input.color.rgb, letterShape * input.color.a);
    output.Normal = 0;
    output.Velocity = EncodeVelocity(input.clipPosition, input.previousClipPosition, CameraToClipSpace, PrevCameraToClipSpace);
    return output;
}
