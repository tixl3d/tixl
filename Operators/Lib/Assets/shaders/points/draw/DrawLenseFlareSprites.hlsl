#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/hash-functions.hlsl"

static const float4 Corners[] = 
{
    //   px py  u v
  float4(-1, -1, 0,1),
  float4( 1, -1, 1,1), 
  float4( 1,  1, 1,0), 
  float4( 1,  1, 1,0), 
  float4(-1,  1, 0,0), 
  float4(-1, -1, 0,1),  
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


cbuffer Params : register(b2)
{
    float4 Color;
    float Size;
    float2 TextureCells;
};

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
};


struct Sprite
{
    float2 PosInClipSpace;
    float2 Size;
    float Rotation;
    float4 Color;
    float2 UvMin;
    float2 UvMax;
    int TextureIndex;
    float2 __padding;
};

sampler texSampler : register(s0);

StructuredBuffer<Sprite> Sprites : t0;
Texture2D<float4> texture2 : register(t1);

psInput vsMain(uint id: SV_VertexID)
{
    psInput output;
    float discardFactor = 1;
    int quadIndex = id % 6;
    int particleId = id / 6;
    float4 cornerFactors = Corners[quadIndex];

    float4 aspect = float4(CameraToClipSpace[1][1] / CameraToClipSpace[0][0],1,1,1);    

    Sprite sprite = Sprites[particleId];

    float3 axis = cornerFactors;
    float2 corner = float2(cornerFactors.x * sprite.Size.x, 
                          cornerFactors.y * sprite.Size.y) * Size;


    float imageRotationRad = (-sprite.Rotation - 90) / 180 * PI;     

    float sina = sin(-imageRotationRad - PI/2);
    float cosa = cos(-imageRotationRad - PI/2);

    corner = float2(
        cosa * corner.x - sina * corner.y,
        cosa * corner.y + sina * corner.x 
    );                              

    float2 p = float2( corner.x / aspect.x + sprite.PosInClipSpace.x,
                       corner.y + sprite.PosInClipSpace.y);

    output.position = float4(p, 0,1);

    // float2 uvMin = 0;
    // float2 uvMax = 1;

    // Texture
    int2 atlasSize = (int2)TextureCells;
    int textureIndex = sprite.TextureIndex % (atlasSize.x * atlasSize.y);
    float2 uvMin = float2(textureIndex % atlasSize.x, textureIndex / atlasSize.x) / atlasSize;
    float2 uvMax = uvMin + 1.0/atlasSize;

    // float textureUx = sprite.TextureIndex;
    // float textureUy = GetUFromMode(TextureAtlasMode, pointId, f, normalizedScatter.wxyz, p.W, output.fog); 
    
    // int textureCelX =  textureUx * atlasSize.x;
    // int textureCelY =  textureUy * atlasSize.y;

    // output.texCoord = (cornerFactors.xy * float2(-1, 1) * 0.5 + 0.5);
    // output.texCoord /= atlasSize;
    // output.texCoord += float2(textureCelX, textureCelY) / atlasSize;    


    output.texCoord = lerp(uvMin, uvMax, cornerFactors.zw);

    output.color = sprite.Color;
    return output;    
}

float4 psMain(psInput input) : SV_TARGET
{
    float4 imgColor = texture2.Sample(texSampler, input.texCoord);
    float4 color = input.color * imgColor * Color;

    return clamp(float4(color.rgb, color.a), 0, float4(1000,1000,1000,1));
}
