//RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float2 NearFarClip;    
    float2 OutputRange;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

float DepthToSceneZ(float depth) 
{
    float n = NearFarClip.x;
    float f = NearFarClip.y;
    return (2.0 * n) / (f + n - depth * (f - n)) * (NearFarClip.y - NearFarClip.x) + NearFarClip.x;    
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float depth=inputTexture.Sample(texSampler, uv).r;

    //float z = DepthToSceneZ(c.x / (OutputRange.y - OutputRange.x) + OutputRange.x );    

    // float n = NearFarClip.x;
    // float f= NearFarClip.y;
    // float z = (2.0 * n) / (f + n - depth * (f - n));
    // float normalizedZ = saturate((z - OutputRange.x) / (OutputRange.y - OutputRange.x));
    //float normalizedZ = z;

    float z = DepthToSceneZ(depth);
    float normalizedZ = (z - OutputRange.x) / (OutputRange.y - OutputRange.x);

    //c.rgb = clamp( c.rgb, 0.000001,1000);


    return float4(normalizedZ.xxx,1);
}
