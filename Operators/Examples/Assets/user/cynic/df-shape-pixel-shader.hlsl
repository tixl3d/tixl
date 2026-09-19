
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
    float Width;
    float Height;
};

Texture2D<float4> InputTexture : register(t0);
Texture2D<float4> AlphaTexture : register(t1);
Texture2D<float4> BackgroundTexture : register(t2);
Texture2D<float4> RefTexture : register(t3);
sampler texSampler : register(s0);
sampler pointSampler : register(s1);


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

float4 psMain(vsOutput input) : SV_TARGET
{
    float2 texCoord = input.texCoord;
    // texCoord = float2(0.85,-0.35) + texCoord*0.15;
    float4 color = BackgroundTexture.Sample(texSampler, texCoord);
    // color = float4(0,0,0,0);
    float4 ref = RefTexture.Sample(texSampler, texCoord);
    // return ref;
    // return float4(color.rgb + ref.rgb, 1);
    float dist = InputTexture.Sample(texSampler, texCoord).r;
    float t = 0.5;
    float aastep;
    float p;
    // aastep = 0.7 * length(float2(ddx(dist), ddy(dist)));
    aastep = 0.5*fwidth(dist);
    p = smoothstep(t - aastep, t + aastep, dist);
    // aastep = fwidth(dist);
    // p = smoothstep(t, t + aastep, dist);
    float alpha = AlphaTexture.Sample(pointSampler, texCoord).r;
    alpha = dist > t - aastep ? p*alpha : 0;
    // p = alpha;
    // p = InputTexture.Sample(pointSampler, texCoord).r;
    // alpha = 1;
    color = float4(color.rgb*(1-alpha) + alpha, 1);
    return color;
}
