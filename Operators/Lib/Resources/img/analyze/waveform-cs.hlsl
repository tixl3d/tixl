cbuffer ParamConstants : register(b0)
{
    float Height;
    float Opacity;
    float ClearBackground;
    float Intensity;
}

sampler texSampler : register(s0);
Texture2D<float4> InputTexture : register(t0);
RWTexture2D<float4> WriteOutput  : register(u0); 

groupshared float4 SharedColors[512];
static const float ToRad = 3.141592/180;

[numthreads(1,512,1)]
void main(uint3 i : SV_DispatchThreadID)
{   
    int texWidth;
    int texHeight;
    WriteOutput.GetDimensions(texWidth, texHeight);

    SharedColors[i.y] = i.y > 256 ? float4(0,0,0, ClearBackground) : 0;

    GroupMemoryBarrierWithGroupSync();

    const int steps = 256;

    if(i.y == 0) 
    {
        float dashColor = i.x % 8 < 4 ? 1 : 0;
        SharedColors[511] = float4(dashColor.xxx, 1);
        SharedColors[256] = float4(dashColor.xxx, 1);

        [fastopt]
        for(int rowIndex= 0; rowIndex < 256; rowIndex++)
        {
            
            float2 uv = float2(float2( i.x / (float)texWidth, (float) rowIndex / 256.0 ));
            float4 col = InputTexture.SampleLevel(texSampler, uv,0);

            int3 level = 512 - clamp( col.rgb * 256, 0, 511);
            SharedColors[level.r] += float4(Intensity,0,0,Opacity) * col.a;
            SharedColors[level.g] += float4(0,Intensity,0,Opacity) * col.a;
            SharedColors[level.b] += float4(0,0,Intensity,Opacity) * col.a;
        }
    }

    GroupMemoryBarrierWithGroupSync();
    WriteOutput[i.xy] = clamp(SharedColors[i.y], 0, 1);
}