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

cbuffer ShadowTransforms : register(b1)
{
    float4x4 Shadow_CameraToClipSpace;
    float4x4 Shadow_ClipSpaceToCamera;
    float4x4 Shadow_WorldToCamera;
    float4x4 Shadow_CameraToWorld;
    float4x4 Shadow_WorldToClipSpace;
    float4x4 Shadow_ClipSpaceToWorld;
    float4x4 Shadow_ObjectToWorld;
    float4x4 Shadow_WorldToObject;
    float4x4 Shadow_ObjectToCamera;
    float4x4 Shadow_ObjectToClipSpace;
};

cbuffer TimeConstants : register(b2)
{
    float GlobalTime;
    float Time;
    float RunTime;
    float BeatTime;
}


Texture2D<float4> ShadowMap0 : register(t0); // opacity shadow map
Texture2D<float4> ShadowMap1 : register(t1); // opacity shadow map
Texture2D<float4> ShadowMap2 : register(t2); // opacity shadow map
Texture2D<float4> ShadowMap3 : register(t3); // opacity shadow map
Texture2D<float4> ColorMap : register(t4); 
sampler texSampler : register(s0);

struct Input
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float4 color : COLOR;
    float4 posInWorld : POSITION;
};

float getOcclusion(Texture2D<float4> shadowMap, float2 uv, float z)
{
    float sz = z;
    float4 color = float4(0.75,0.6,0.4,1);
    float4 om = shadowMap.SampleLevel(texSampler, uv, 0);
    float4 mask = saturate((float4(sz,sz,sz,sz) - float4(0.00, 0.25, 0.50, 0.75)) * 4.0);
    om *= mask;
    float occlusion = om.x + om.y + om.z + om.w;

    return occlusion;
}

float4 psMain(Input input) : SV_TARGET
{
    float4 particleInClipSpace = mul(input.posInWorld, Shadow_WorldToClipSpace);
    particleInClipSpace.xyz /= particleInClipSpace.w;
    particleInClipSpace.xy = particleInClipSpace.xy*0.5 + 0.5;
    particleInClipSpace.y = 1.0 - particleInClipSpace.y;
    float sz = particleInClipSpace.z - 0.25*0.25; // 4 textures with 4 channels, so 1/16 slice offset

    float occlusion = 0.0;
    occlusion += getOcclusion(ShadowMap0, particleInClipSpace.xy, clamp(particleInClipSpace.z, 0, 0.25)*4.0);
    occlusion += getOcclusion(ShadowMap1, particleInClipSpace.xy, 4.0*(clamp(particleInClipSpace.z, 0.25, 0.5) - 0.25));
    occlusion += getOcclusion(ShadowMap2, particleInClipSpace.xy, 4.0*(clamp(particleInClipSpace.z, 0.5, 0.75) - 0.5));
    occlusion += getOcclusion(ShadowMap3, particleInClipSpace.xy, 4.0*(clamp(particleInClipSpace.z, 0.75, 1.0) - 0.75));
    occlusion *= 0.25;
    occlusion = 1.0 - saturate(occlusion);

    // simple diffuse light
    // float3 lightPosInWorld = float3(cos(RunTime)*15.0, 15.0, sin(RunTime)*15.0);
    // float3 dir = (lightPosInWorld - input.posInWorld.xyz);
    // float dist = length(dir);
    // dir /= dist;
    // float diffuse = saturate(dot(dir, float3(0,1,0)));
    // occlusion *= diffuse;

// float3 lightColor = float3(1,1,1)*190.0;
    float4 color = input.color * ColorMap.Sample(texSampler, input.texCoord);
    // if (color.a < 0.35)
        // discard;
    float2 p = input.texCoord * float2(2.0, 2.0) - float2(1.0, 1.0);
    if (dot(p, p) > 1.0)
         discard;
    // color.rgb *= lightColor/(dist*dist);
    // color = float4(dir, 1);
    // color = float4(diffuse, diffuse, diffuse, 1);
    color.rgb *= occlusion;
    


    // color.a = 0.2;

    return color;
}
