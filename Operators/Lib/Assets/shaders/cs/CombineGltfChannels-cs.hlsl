Texture2D<float4> MetallicRoughness : register(t0);
Texture2D<float4> Occlusion : register(t1);

RWTexture2D<float4> Result : register(u0);
sampler texSampler : register(s0);

[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint width, height;
    Result.GetDimensions(width, height);

    float2 uv = float2( (float)i.x/width, (float)i.y/height );
    float4 c = float4(
        MetallicRoughness.SampleLevel(texSampler, uv, 0).gb,
        Occlusion.SampleLevel(texSampler, uv, 0).r,
        1
     );

    Result[i.xy] = c;
}

