Texture2D<float4> colorTexture : register(t0);
Texture2D<float> depthTexture : register(t1);
RWTexture2D<float2> outputTexture : register(u0);

sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float Near;
    float Far;
    float FocusCenter;
    float FocusRange;
    float MaxBlurSize;
    float RadiusScale;
}


[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    int width, height;
    colorTexture.GetDimensions(width, height);
    float2 pixelSize = 1.0 / float2(width, height);
    
    float range = 1.0 / (Far - Near);
    float fc = FocusCenter * range;
    float fr = FocusRange * range;
    float d = depthTexture[i.xy];

    float nearWeight = (fc - d)/fr;
    float farWeight = (d - fc)/fr;
    // nearWeight = 0;
    // farWeight = 0;


    outputTexture[i.xy] = float2(nearWeight, farWeight);
}

