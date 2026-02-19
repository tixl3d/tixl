
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
    float4 HighlightColor;
    float StrokeCount;
    float RangeSize;
    float RangeOffset;
    float StrokeRatio;
    float Feather;
    float HighlightIndex;
    float Shift;
};

struct GridEntry
{
    // float2 gridPos;
    // float2 charUv;
    // float highlight;
    // float3 __filldummy;
    //float2 size;
    //float2 __filldummy;

    float3 Position;
    float Size;
    float3 Orientation;
    float AspectRatio;
    float4 Color;
    float4 UvMinMax;
    float BirthTime;
    float Speed;
    uint Id;        
};

// struct PsInput
// {
//     float4 position : SV_POSITION;
//     float2 texCoord : TEXCOORD;
//     float4 color : COLOR;
//     float entryIndex: TEXCOORD;
// };

struct PsInput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float4 color : COLOR;
    float entryIndex: TEXCOORD1;
};


StructuredBuffer<GridEntry> GridEntries : t0;
Texture2D<float4> fontTexture : register(t1);
sampler texSampler : register(s0);


PsInput vsMain(uint id: SV_VertexID)
{
    PsInput output;

    int vertexIndex = id % 6;
    int entryIndex = id / 6;
    float3 quadPos = Quad[vertexIndex];

    GridEntry entry = GridEntries[entryIndex];

    float3 posInObject = entry.Position;
    posInObject.xy += quadPos.xy * float2(entry.Size * entry.AspectRatio, entry.Size) ; //CellSize *  (1- CellPadding) * (1+overrideScale* OverrideScale) /2;
    float4 quadPosInWorld = mul(float4(posInObject.xyz,1), ObjectToWorld);    
    float4 quadPosInCamera = mul(quadPosInWorld, WorldToCamera);
    output.position = mul(quadPosInCamera, CameraToClipSpace);
    float4 uv = entry.UvMinMax * UV[vertexIndex];
    output.texCoord =  uv.xy + uv.zw;
    output.entryIndex = entryIndex;
    return output;
}

float mod(float x, float y) {
    return ((x) - (y) * floor((x) / (y)));
} 



float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

float4 psMain(PsInput input) : SV_TARGET
{    
    float4 texColor = fontTexture.Sample(texSampler, input.texCoord);
    float2 msdfUnit = float2(1,1);
    float3 smpl1 =  fontTexture.Sample(texSampler, input.texCoord).rgb;
        
    int height, width;
    fontTexture.GetDimensions(width,height);

    float dx = ddx( input.texCoord.x ) * width;
    float dy = ddy( input.texCoord.y ) * height;
    float toPixels = 8.0 * rsqrt( dx * dx + dy * dy );
    float sigDist = median( smpl1.r, smpl1.g, smpl1.b ) - 0.5;

    int steps=  clamp(StrokeCount, 1,10);
    float d= RangeOffset - RangeSize/2;
    //float dd = 0.1;
    float feather = Feather;
    float stepWidth = (float)RangeSize/(float)steps;
    float strokeWidth = stepWidth * StrokeRatio;

    float letterShape = 0;
    float4 outColor = float4(0,0,0,0);
    //outColor += Color;

    float outerRange = smoothstep( RangeOffset - RangeSize/2 -feather, RangeOffset-RangeSize/2 , sigDist) * smoothstep( RangeOffset+RangeSize/2 +feather, RangeOffset+ RangeSize/2, sigDist);

    //return float4(outerRange,0,0,1);
    float shiftedDist = sigDist + mod(Shift* stepWidth, stepWidth);
    

    float sum =0;
    for(int i=0; i<= steps; i++) 
    {        
        float a = smoothstep( d, d+feather, shiftedDist) * smoothstep( d+strokeWidth+feather, d+strokeWidth, shiftedDist) * outerRange;
        sum += a;
        float4 c= (((i + HighlightIndex +Shift + input.entryIndex) % steps) > 1) ?  Color : HighlightColor;
        outColor += c * a * float4(1,1,1,a);
        d+= stepWidth;
    }
    //return float4(sum*outerRange,0, 0,1);

    //return float4(Color.rgb, letterShape * Color.a);
    return outColor;
}
