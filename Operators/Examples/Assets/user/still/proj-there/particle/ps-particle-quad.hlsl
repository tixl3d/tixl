struct Input
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
    float3 objectPos: POSITIONT;
    float3 posInWorld: POSITION2;
    float3 velocity: POSITION3;
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
    float3 LightPosition;

    float LightIntensity;
    float LightDecay;
    float RoundShading;
};

#define mod(x,y) (x-y*floor(x/y))

struct Output
{
    float4 color : SV_Target;
    float4 velocity : SV_TARGET1;
};

Output psMain(Input input) 
{
    float2 p = input.texCoord * float2(2.0, 2.0) - float2(1.0, 1.0);
    float d= dot(p, p);
    if (d > 1.0)
         discard;
   
    float z = sqrt(1 - d*d);
    float3 normal = float3(p, z);
    float3 lightDir = normalize(LightPosition - input.posInWorld.xyz);
    //lightDir = mul(float4(lightDir,1), ObjectToWorld);
    normal = mul(float4(normal,0), CameraToWorld).xyz;

    float diffuse = lerp(1, saturate(dot(normal, lightDir)), RoundShading);
    //float3 ambient = float3(diffuse, diffuse, diffuse);
    //return color;

    // float3 lightDirectionInWorld = normalize(LightPosition - input.objectPos);
    // float dAtFragment = sqrt(1- d * d);
    // float3 n = normalize(float3(p.xy * float2(1,1), dAtFragment ));
      
    // float4 nInWorld = normalize(mul(float4(n,1), CameraToWorld));
    // float4 stripes = float4(1,1,1,0) * (mod( nInWorld*10,1) > 0.12 ? 0 :0.5);
    // return float4(nInWorld.rgb,1) + stripes;

    // // TEST
    // float ambient = saturate(dot(lightDirectionInWorld.xyz, nInWorld.xyz));
    // float3 specularColor =  input.color.rgb * pow(ambient, 30) *0; // HACK TO DISABLE

    Output output;
    output.color = float4(input.color.rgb * diffuse, 1);
    output.velocity = float4(input.velocity.rg,0,1);

    return output;
    // JUNK
}
