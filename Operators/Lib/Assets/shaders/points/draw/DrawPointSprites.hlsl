#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/SpriteDef.hlsl"

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

struct psInput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float4 color : COLOR;
};

StructuredBuffer<Point> Points : t0;
StructuredBuffer<SpriteDef> Sprites : t1;
Texture2D<float4> fontTexture : register(t2);
sampler texSampler : register(s0);


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

    float3 quadCorners = Quad[vertexIndex];

    float sizeFxFactor = ScaleFactor == 0
                             ? 1
                         : (ScaleFactor == 1) ? p.FX1
                                          : p.FX2;

    float3 s = Size * sizeFxFactor * (UsePointScale ? p.Scale : 1);


    float3 posInObject =  (-float3(sprite.Pivot, 0) + quadCorners * float3(sprite.Size,0)) * s;

    float4x4 orientationMatrix = transpose(qToMatrix(p.Rotation));
    posInObject = mul( float4(posInObject.xyz, 1), orientationMatrix);
    posInObject += p.Position;

    float4 quadPosInWorld = mul(float4(posInObject.xyz,1), ObjectToWorld);
    float4 quadPosInCamera = mul(quadPosInWorld, WorldToCamera);
    output.position = mul(quadPosInCamera, CameraToClipSpace);

    float4 uv = float4(sprite.UvMin, sprite.UvMax) * UV[vertexIndex];
    output.texCoord =  uv.xy + uv.zw;
  
    output.color =  Color * p.Color;
    return output;
}


float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

float4 psMain(psInput input) : SV_TARGET
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
    if(AlphaCutOff > 0 && letterShape < AlphaCutOff) {
        discard;
    }
    
    return float4(input.color.rgb, letterShape * input.color.a);
}
