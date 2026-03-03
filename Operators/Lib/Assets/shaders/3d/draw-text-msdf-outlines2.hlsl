
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
    float4 OutlineColor;
    float4 FillColor;
    float Inset;
    float Outset;
    float Sharpness;
    float BillboardMode;
};

struct GridEntry
{
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

    // First, get the letter's position in object space
    float3 letterPos = entry.Position;

    // Add the quad offset to create the vertex position in object space
    float3 vertexPos = letterPos;
    vertexPos.xy += quadPos.xy * float2(entry.Size * entry.AspectRatio, entry.Size);

    if (BillboardMode > 0.5)
    {
        float4 layoutCenter = float4(0, 0, 0, 1);
        float4 worldCenter = mul(layoutCenter, ObjectToWorld);

        // Transform the anchor to camera space
        float4 camCenter = mul(worldCenter, WorldToCamera);

        float3 localOffset = letterPos;
        localOffset.xy += quadPos.xy * float2(entry.Size * entry.AspectRatio, entry.Size);

        // Apply the offset directly in camera space — axes are already screen-aligned
        camCenter.xy += localOffset.xy;
        output.position = mul(camCenter, CameraToClipSpace);
    }
    else
    {
        // Transform the vertex position to world space
        float4 worldPos = mul(float4(vertexPos, 1), ObjectToWorld);

        // Then transform to camera space
        float4 camPos = mul(worldPos, WorldToCamera);

        // Finally, transform to clip space
        output.position = mul(camPos, CameraToClipSpace);
    }

    float4 uv = entry.UvMinMax * UV[vertexIndex];
        output.texCoord = uv.xy + uv.zw;

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
 
    float3 smpl1 =  fontTexture.Sample(texSampler, input.texCoord).rgb;


    int height, width;
    fontTexture.GetDimensions(width,height);

    // from https://github.com/Chlumsky/msdfgen/issues/22#issuecomment-234958005

    float2 dx2 = abs(ddx( input.texCoord.xy ) * width);
    float2 dy2 = abs(ddy( input.texCoord.xy ) * height);
    float dx= max(dx2.x, dx2.y);
    float dy= max(dy2.x, dy2.y);
    float edge = rsqrt( dx * dx + dy * dy );

    float toPixels = Sharpness * edge ;
    float sigDist = median( smpl1.r, smpl1.g, smpl1.b ) - 0.5;
    float letterShape = clamp( sigDist * toPixels + 0.5, 0.0, 1.0 );
    //sigDist = max(min(sigDist2,sigDist),-max(sigDist2,sigDist));
    

    float outerEdge = sigDist + Outset;
    float innerEdge = Inset - sigDist;      
    float outlineSd = min(outerEdge, innerEdge);
    float outline = clamp(outlineSd * toPixels + 0.5, 0.0, 1.0);
    float fill = clamp((sigDist + Outset) * toPixels + 0.5, 0.0, 1.0);

    if (FillColor.a < 0.01)
    {
        return float4(OutlineColor.rgb, outline);
    }
  
   

    float3 rgb = lerp(OutlineColor.rgb, FillColor.rgb, fill-outline);
    float a    = max(outline * OutlineColor.a, fill * FillColor.a);

    return float4(rgb, a);
}
