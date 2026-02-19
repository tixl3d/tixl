
static const float3 Quad[] = 
{
  float3(0, -1, 0),
  float3( 1, -1, 0), 
  float3( 1,  0, 0), 
  float3( 1,  0, 0), 
  float3(0,  0, 0), 
  float3(0, -1, 0), 

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
   // float4 Shadow;
    float Inset;
    float Outset;
};

struct GridEntry
{
    // float2 gridPos;
    // float2 charUv;
    // float highlight;
    // float3 __filldummy;
    //float2 size;
    //float2 __filldummy;

    // float3 Position;
    // float Size;
    // float3 Orientation;
    // float AspectRatio;
    // float4 Color;
    // float4 UvMinMax;
    // float BirthTime;
    // float Speed;
    // uint Id;        

    float3 Position;     
    float Size;             // 3
    float AspectRatio;      // 4
    float4 Orientation;     // 5
    float4 Color;           // 9
    float4 UvMinMax;        // 13
    uint CharIndex;         // 17
    uint LineNumber;        // 18
    float2 Offset;
};

struct Output
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float4 color : COLOR;
};

StructuredBuffer<GridEntry> GridEntries : t0;
Texture2D<float4> fontTexture : register(t1);
sampler texSampler : register(s0);


Output vsMain(uint id: SV_VertexID)
{
    Output output;

    int vertexIndex = id % 6;
    int entryIndex = id / 6;
    float3 quadPos = Quad[vertexIndex];


    GridEntry entry = GridEntries[entryIndex];


    float3 posInObject = entry.Position;
    posInObject.xy += quadPos.xy * float2(entry.Size * entry.AspectRatio, entry.Size);

    // Experimenting with aligned font scaling (not working)
    // float2 posInQuad = quadPos.xy * float2(entry.Size * entry.AspectRatio, entry.Size);
    // posInQuad -= entry.Offset / 1024 * TestParams.x + float2(0.5 * entry.AspectRatio, -0.22) * TestParams.z;
    // posInQuad *= TestParams.y;
    // posInQuad += entry.Offset / 1024 * TestParams.x - float2(0.25 * entry.AspectRatio, -1.22) * TestParams.z * TestParams.y;
    // posInQuad -= 0.08;
    // float3 posInObject = float3(posInQuad,0) + entry.Position;


    float4 quadPosInWorld = mul(float4(posInObject.xyz,1), ObjectToWorld);
    
    //quadPosInWorld.xy += quadPos.xy * float2(entry.Size * entry.AspectRatio, entry.Size) ; //CellSize *  (1- CellPadding) * (1+overrideScale* OverrideScale) /2;
    
    float4 quadPosInCamera = mul(quadPosInWorld, WorldToCamera);
    output.position = mul(quadPosInCamera, CameraToClipSpace);
    //output.position.z = 0;
    //output.color = lerp(Color, HighlightColor, entry.highlight) * overrideBrightness;
    //output.texCoord = (entry.charUv + quadPos * float2(0.5, -0.5) + 0.5)/16;
    float4 uv = entry.UvMinMax * UV[vertexIndex];
    output.texCoord =  uv.xy + uv.zw;
    return output;
}


struct PsInput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float4 color : COLOR;
};

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

float4 psMain(PsInput input) : SV_TARGET
{    
    //float2 msdfUnit = float2(1,1) * 1;// pxRange/float2(textureSize(msdf, 0));
    float3 smpl1 =  fontTexture.Sample(texSampler, input.texCoord).rgb;
    //return float4(smpl1,1);
    // float sigDist1 = median(smpl1.r, smpl1.g, smpl1.b) - 0.0001;
    // float opacity1 = smoothstep(0.0,0.9,sigDist1*sigDist1);
    //return float4(opacity1.xxx,1);

    int height, width;
    fontTexture.GetDimensions(width,height);

    // from https://github.com/Chlumsky/msdfgen/issues/22#issuecomment-234958005

    float2 dx2 = abs(ddx( input.texCoord.xy ) * width);
    float2 dy2 = abs(ddy( input.texCoord.xy ) * height);
    float dx= max(dx2.x, dx2.y);
    float dy= max(dy2.x, dy2.y);
    float edge = rsqrt( dx * dx + dy * dy ) + Outset;

    float toPixels = 16 * edge ;
    //float sigDist = median( smpl1.r, smpl1.g, smpl1.b ) - 0.5;
    float sigDist = median( smpl1.r, smpl1.g, smpl1.b ) - 0.5 + Outset;
    float sigDist2 = median( smpl1.r, smpl1.g, smpl1.b ) - 0.5 - Inset;
    //sigDist = abs(sigDist) + Outset;
    sigDist = -max(-sigDist,sigDist2);
    //sigDist = max(min(sigDist2,sigDist),-max(sigDist2,sigDist));
    float letterShape = clamp( sigDist * toPixels + 0.5, 0.0, 1.0 );
  
    float4 Shadow = float4(1,1,1,0);
     if(Shadow.a < 0.01) {
        return float4(Color.rgb, letterShape * Color.a);
    }

    float glow = pow( smoothstep(0, 1, sigDist + 0.3), 0.4);
    //return float4(letterShape,0,0,1);

    return float4(
        lerp(Shadow.rgb, Color.rgb, saturate(pow(letterShape,0.3)) ),
        max( saturate(letterShape*2),glow * Shadow.a) * Color.a
    ); 
}
