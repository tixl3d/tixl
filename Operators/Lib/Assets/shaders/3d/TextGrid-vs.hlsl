
static const float3 Quad[] = 
{
  float3(-1, -1, 0),
  float3( 1, -1, 0), 
  float3( 1,  1, 0), 
  float3( 1,  1, 0), 
  float3(-1,  1, 0), 
  float3(-1, -1, 0), 
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
    float2 GridSize;
    float2 CellSize;

    float2 CellPadding;
    float2 TextOffset;
    float4 Color;

    float3 OverridePosition;
    float OverrideScale;

    float4 HighlightColor;
    float OverrideBrightness;
};

struct GridEntry
{
    float2 gridPos;
    float2 charUv;
    float highlight;
    float3 __filldummy;
    //float2 size;
    //float2 __filldummy;
};

struct Output
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float4 color : COLOR;
};

StructuredBuffer<GridEntry> GridEntries : t0;
Texture2D<float4> displaceTexture : register(t1);
sampler texSampler : register(s0);


Output vsMain(uint id: SV_VertexID)
{
    Output output;

    int vertexIndex = id % 6;
    int entryIndex = id / 6;
    float3 quadPos = Quad[vertexIndex];


    GridEntry entry = GridEntries[entryIndex];
    float2 samplePos = float2(0,1)+entry.gridPos * float2(1,-1);

    float4 overrideColor = displaceTexture.SampleLevel(texSampler, samplePos - (TextOffset.xy * float2(1,-1) % 1) / GridSize, 0);
    overrideColor = clamp(overrideColor, 0, float4(1,100,100,1));    
    float overrideDisplace = overrideColor.b;
    float overrideScale = overrideColor.g;
    float overrideBrightness = clamp((overrideColor.r * 0.5 + overrideColor.b * 0.3 + overrideColor.g * 0.2) * overrideColor.a,0,1);

    float2 centeredGridPos = float2( (entry.gridPos.x - 0.5) * GridSize.x, 
                                    (-0.5 + entry.gridPos.y ) * GridSize.y
                                );
    centeredGridPos.xy +=  TextOffset.xy * float2(-1,1) % 1;

    float3 posInObject =  float3( centeredGridPos * CellSize,0 );

    //objectPos += float3(GridSize.x *-0.5, +GridSize.y * 0.5 ,0);
    posInObject+= float3( overrideDisplace * OverridePosition);

    float4 quadPosInWorld = mul(float4(posInObject.xyz,1), ObjectToWorld);
    
    quadPosInWorld.xy += quadPos.xy * CellSize *  (1- CellPadding) * (1+overrideScale* OverrideScale) /2;
    
    float4 quadPosInCamera = mul(quadPosInWorld, WorldToCamera);
    output.position = mul(quadPosInCamera, CameraToClipSpace);
    //output.position.z = 0;
    output.color = lerp(Color, HighlightColor, entry.highlight) * overrideBrightness;
    output.texCoord = (entry.charUv + quadPos * float2(0.5, -0.5) + 0.5)/16;
    return output;
}