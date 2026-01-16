cbuffer ParamConstants : register(b0)
{
    float4x4 TransformMatrix;
    float4 LineColor;
    float4 BackgroundColor;
}

cbuffer Transforms : register(b1)
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
}

cbuffer Params : register(b2)
{
    /*{FLOAT_PARAMS}*/
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float3 posInWorld : POSITION;
    float distToCamera : DEPTH;
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
    vsOutput output;
    float2 quadVertex = Quad[vertexId].xy;
    float2 quadVertexInObject = quadVertex;
    float4 posInObj = mul(float4(quadVertexInObject, 0, 1), TransformMatrix);

    output.position = mul(posInObj, ObjectToClipSpace);
    output.posInWorld = mul(posInObj, ObjectToWorld).xyz;
    output.texCoord = quadVertex * float2(0.5, -0.5) + 0.5;
    return output;
}

sampler ClampedSampler : register(s0);
sampler WrappedSampler : register(s1);

//=== Resources =====================================================
/*{RESOURCES(t0)}*/

//=== Globals =======================================================
/*{GLOBALS}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------
float4 GetField(float4 p)
{
    p.xyz = mul(float4(p.xyz, 1), WorldToObject).xyz;
    float4 f = 1;
    /*{FIELD_CALL}*/
    return f;
}

float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}

//===================================================================

const static float NormalSamplingDistance = 0.01;

float3 GetNormalNonNormalized(float3 p)
{
    // return normalize(
    return GetDistance(p + float3(NormalSamplingDistance, -NormalSamplingDistance, -NormalSamplingDistance)) * float3(1, -1, -1) +
           GetDistance(p + float3(-NormalSamplingDistance, NormalSamplingDistance, -NormalSamplingDistance)) * float3(-1, 1, -1) +
           GetDistance(p + float3(-NormalSamplingDistance, -NormalSamplingDistance, NormalSamplingDistance)) * float3(-1, -1, 1) +
           GetDistance(p + float3(NormalSamplingDistance, NormalSamplingDistance, NormalSamplingDistance)) * float3(1, 1, 1);
    //);
}

#define PI acos(-1.)
#define INFINITY pow(2., 8.)

float4 DistanceMeter(float dist, float rayLength, float3 rayDir, float camHeight, float horizonFade)
{
    float idealGridDistance = camHeight;
    float nearestBase = floor(log(idealGridDistance) / log(10.0));
    float relativeDist = abs(dist / camHeight);

    float largerDistance = pow(10.0, nearestBase + 1.0);
    float smallerDistance = pow(10.0, nearestBase);

    float3 col = 1;
    col = max(float3(0.0, 0.0, 0.0), col);
    if (sign(dist) < 0.0)
    {
        // col = col.grb * 0.1;
    }

    float l0 = pow(0.5 + 0.5 * cos(dist * 3.14159265359 * 2.0 * smallerDistance), 20.0);
    float l1 = pow(0.5 + 0.5 * cos(dist * 3.14159265359 * 2.0 * largerDistance), 5.0);
    float l = log(idealGridDistance) / log(10.0);
    float x = frac(l);
    l0 = lerp(l0, 0.0, smoothstep(0.4, 1.0, x));
    l1 = lerp(0.0, l1, smoothstep(0.0, 0.4, x));

    float lines = pow(1 - (0.01 + 0.99 * (1.0 - l0) * (1.0 - l1)), 20);
    float cutline = pow(1 - clamp(abs(dist), 0, 1) + 0.01 / camHeight, 20);

    float inside = sign(dist) < 0 ? 0.3 : 0;

    float lineAlpha = (lines * 0.7 + cutline + inside) * horizonFade;
    return lerp(BackgroundColor, LineColor, lineAlpha);
}

float4 psMain(vsOutput input) : SV_TARGET
{
    float dist = GetDistance(input.posInWorld);
    float2 s = length(float2(ddy(input.texCoord.x), ddy(input.texCoord.y)));

    float3 camPos = mul(float4(0, 0, 0, 1), CameraToWorld).xyz;
    float dToCam = length(camPos - input.posInWorld);
    float3 ray = float3(0, 0, 1);

    float sideView = saturate(max(abs(s.x), abs(s.y)) * 1000);

    float lipschitz = pow(length(GetNormalNonNormalized(input.posInWorld)) - 0.0, 0.5);
    // return float4( lipschitz.xxx,1);
    return DistanceMeter(dist, 0.1, ray, 15 / dToCam, 1 - sideView);
    // + float4( lipschitz.xxx,1);
    //+ float4( length(lipschitz).xxx / 3,1);
}
