cbuffer ParamConstants : register(b0)
{
    float MaxSteps;
    float StepSize;
    float MinDistance;
    float MaxDistance;

    float Fog;
    float DistToColor;
    float AODistance;
    float __padding1;

    float4 Specular;
    float4 Glow;
    float4 AmbientOcclusion;
    float4 Background;

    float3 LightPos;
    float __padding;

    float2 Spec;
}

cbuffer Params : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

struct vsOutput
{
    // float4 position : COLOR;
    float2 texCoord : TEXCOORD;
    // float3 viewDir : VPOS;
    // float3 worldTViewDir : TEXCOORD1;
    // float3 worldTViewPos : TEXCOORD2;
};

static const float3 Quad[] =
    {
        float3(-1, -1, 0),
        float3(1, -1, 0),
        float3(1, 1, 0),
        float3(1, 1, 0),
        float3(-1, 1, 0),
        float3(-1, -1, 0),
};

vsOutput vsMain4(uint vertexId : SV_VertexID)
{
    float4 quadPos = float4(Quad[vertexId], 1);
    float2 texCoord = quadPos.xy * float2(0.5, -0.5) + 0.5;

    vsOutput output;
    output.texCoord = texCoord;
    // output.texCoord = texCoord;
    // output.position = quadPos;
    // float4x4 ViewToWorld = ClipSpaceToWorld; // CameraToWorld ;

    // float4 viewTNearFragPos = float4(texCoord.x * 2.0 - 1.0, -texCoord.y * 2.0 + 1.0, 0.0, 1.0);
    // float4 worldTNearFragPos = mul(viewTNearFragPos, ViewToWorld);
    // worldTNearFragPos /= worldTNearFragPos.w;

    // float4 viewTFarFragPos = float4(texCoord.x * 2.0 - 1.0, -texCoord.y * 2.0 + 1.0, 1.0, 1.0);
    // float4 worldTFarFragPos = mul(viewTFarFragPos, ViewToWorld);
    // worldTFarFragPos /= worldTFarFragPos.w;

    // output.worldTViewDir = normalize(worldTFarFragPos.xyz - worldTNearFragPos.xyz);
    // output.worldTViewPos = worldTNearFragPos.xyz;

    // output.viewDir = -normalize(float3(CameraToWorld._31, CameraToWorld._32, CameraToWorld._33));

    return output;
}

/*{FIELD_FUNCTIONS}*/

// struct VS_IN
// {
//     float4 pos : POSITION;
//     float2 texCoord : TEXCOORD;
// };

// struct PS_IN
// {
//     float4 pos : SV_POSITION;
//     float2 texCoord : TEXCOORD0;
//     float3 viewDir;
//     float3 worldTViewPos : TEXCOORD1;
//     float3 worldTViewDir : TEXCOORD2;
// };

//---------------------------------------
float4 GetColor(float2 pos)
{
    return /*{FIELD_CALL}*/ 1;
}
//---------------------------------------------------

float4 psMain(vsOutput input) : SV_TARGET
{
    return GetColor(input.texCoord);
}
