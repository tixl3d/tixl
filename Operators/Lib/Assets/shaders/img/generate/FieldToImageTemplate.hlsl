#include "shared/bias-functions.hlsl"

// Parameteres of the field
cbuffer Params : register(b0)
{
    /*{FLOAT_PARAMS}*/
}

// Parameters of the OP
cbuffer ParamConstants : register(b1)
{
    float2 Center;
    float Scale;
    float Rotate;
    float2 GainAndBias;
    float2 Range;
    float SliceDepth;
}

// int paramet const buffer
cbuffer ParamConstants : register(b2)
{
    int PingPong;
    int Repeat;
    int Mode;
}

// resolution const buffer? -> YES for aspect ratio
cbuffer ResolutionConstBuffer : register(b3)
{
    float TargetWidth;
    float TargetHeight;
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
    float4 quadPos = float4(Quad[vertexId], 1);
    output.texCoord = quadPos.xy * float2(0.5, -0.5) + 0.5;
    output.position = quadPos;
    return output;
}

sampler ClampedSampler : register(s0);

Texture2D<float4> Gradient : register(t0);

float PingPongRepeat(float x, float pingPong, float repeat)
{
    float baseValue = x;
    float repeatValue = frac(baseValue);
    float pingPongValue = 1.0 - abs(frac(x * 0.5) * 2.0 - 1.0);
    float singlePingPong = abs(x);

    // Select pingpong type: single or repeating
    float pingPongOutput = lerp(singlePingPong, pingPongValue, step(0.5, repeat));

    // Select between base, repeat, or pingpong
    float value = lerp(baseValue, repeatValue, step(0.5, repeat)); // If repeat, use repeatValue
    value = lerp(value, pingPongOutput, step(0.5, pingPong));      // If pingpong, override with pingpong

    // Clamp final result if not repeating
    value = lerp(saturate(value), value, step(0.5, repeat)); // If NOT repeating, clamp to [0..1]

    return value;
}

//=== Resources =====================================================
// This needs to be set to the first free texture resource index for that template
/*{RESOURCES(t1)}*/

//=== Globals =======================================================
/*{GLOBALS}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------
float4 GetField(float4 p)
{
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
    return GetDistance(p + float3(NormalSamplingDistance, -NormalSamplingDistance, -NormalSamplingDistance)) * float3(1, -1, -1) +
           GetDistance(p + float3(-NormalSamplingDistance, NormalSamplingDistance, -NormalSamplingDistance)) * float3(-1, 1, -1) +
           GetDistance(p + float3(-NormalSamplingDistance, -NormalSamplingDistance, NormalSamplingDistance)) * float3(-1, -1, 1) +
           GetDistance(p + float3(NormalSamplingDistance, NormalSamplingDistance, NormalSamplingDistance)) * float3(1, 1, 1);
}

float4 psMain(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;
    float aspectRatio = TargetWidth / TargetHeight;
    uv.y = 1.0 - uv.y; // Flip Y for correct orientation
    uv -= 0.5;
    uv -= Center;
    uv.x *= aspectRatio;
    uv *= 2.0; // Scale UV to cover the whole area from -1 to 1
    float a = radians(Rotate);
    uv = cos(a) * uv + sin(a) * float2(uv.y, -uv.x);

    uv /= Scale;

    float4 samplePos = float4(uv, SliceDepth, 0);
    float4 f = GetField(samplePos);

    float d = f.w;

    if (Mode == 1)
        return float4(f.rgb, 1);

    d = (d - Range.x) / (Range.y - Range.x);

    d = PingPongRepeat(d, PingPong, Repeat);
    d = ApplyGainAndBias(d, GainAndBias);

    float4 color = Gradient.Sample(ClampedSampler, float2(d, 0.5));

    // float4 color = float4(f.rgb, 1); // Grayscale with 1 alpha
    return color;
}
