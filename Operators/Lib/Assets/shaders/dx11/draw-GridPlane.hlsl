
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
    float4 Color;
    float Size;
    float Scale;
};

sampler texSampler : register(s0);


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float2 scale: TEXCOORD2;
};


#define mod(x,y) (x-y*floor(x/y))

vsOutput vsMain(uint vertexId: SV_VertexID)
{
    vsOutput output;
    float2 quadVertex = Quad[vertexId].xy;
    float2 quadVertexInObject = quadVertex * Size * 0.5;

    output.position = mul(float4(quadVertexInObject, 0, 1), ObjectToClipSpace);
    //output.position.xyz /= output.position.w;

    output.texCoord = quadVertex*float2(0.5, -0.5) + 0.5;
    output.scale = mul(float4(1, 1,1,1), WorldToObject).xy / Size;
    return output;
}


static float divisions = 1;
static float lineWidth = 0.3;
static float feather = 0.1;
//static float divs = 0.1;


float lines(float d, float angle, float spacing) {
    float pInCel = mod(d, spacing)/ spacing;

    float distanceToEdge = abs(pInCel - 0.5);

    float feather2 = feather * angle * 15 / spacing;    
    float lineWidth2 =  angle / spacing * lineWidth;
    return  1-smoothstep(lineWidth2 - feather2 , lineWidth2 + feather2, 0.5-distanceToEdge) ;
}

float4 psMain(vsOutput input) : SV_TARGET
{
    float angleX = fwidth(input.texCoord.x) / input.scale;
    float angleY = fwidth(input.texCoord.y) / input.scale;

    float2 p = (input.texCoord -0.5) * divisions / input.scale;
    float combinedAngle = max(angleX, angleY);
    float fadeOutInDistance = 1-pow( saturate( combinedAngle), 0.5);

    float smallGrid =  smoothstep( 0.4, 0.9, fadeOutInDistance);
    float grid10 = smoothstep( -0.6, 1.1, fadeOutInDistance);
    float axis = smoothstep( -3, 0.3, fadeOutInDistance);

    float linesX = lines(p.x, angleX, 1* Scale) * smallGrid + lines(p.x, angleX, 10* Scale) *grid10 + lines(p.x, angleX, 100* Scale);// * axis * (p.x > 0 ? 1:0* Scale);
    float linesY =  lines(p.y, angleY, 1* Scale) * smallGrid + lines(p.y, angleY, 10* Scale) * grid10 +  lines(p.y, angleY, 100* Scale);// * axis *  (p.y > 0 ? 1:0);;

    float lines = max(linesX, linesY);

    float axisX = smoothstep( -0.1, 0.1, saturate(abs(p.y) * (lineWidth + angleY * 100) *1));
    float axisZ = smoothstep( -0.1, 0.1, saturate(abs(p.x) * ( (p.x > 0 ? 2:1)*  lineWidth + angleX * 100) *1 ));

    float redOrBlue = (axisX < axisZ) ? 0:1;
    float3 axisColor = lerp( 
        p.x > 0 ? float3(0.8,0,0) : float3(0.8, 0.6, 0.6),
        p.y < 0 ? float3(0.2,0.2,0.9) : float3(0.5, 0.5, 0.7),  
        redOrBlue);
    float isAxis = min(axisZ, axisX) < 0.75 ? 1:0;
    float3 color = lerp( float3(1,1,1), axisColor, isAxis );
    return float4(color, lines * (isAxis ? 2 : 1)) * Color;
}


float4 psMainOnlyColor(vsOutput input) : SV_TARGET
{
    return float4(1,1,0,1); //saturate(Color);
}
