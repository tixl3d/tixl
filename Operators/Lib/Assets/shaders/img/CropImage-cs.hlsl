#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"


cbuffer Params : register(b0)
{
    float CropLeft;
    float CropRight;
    float CropTop;
    float CropBottom;
    float4 BackgroundColor; 
}

Texture2D<float4> SourceImage : register(t0);
RWTexture2D<float4> Result : register(u0);


[numthreads(8,8,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    int width,height, numLevels;
    SourceImage.GetDimensions(0, width, height, numLevels);

    int x = i.x - int(CropLeft+0.4);
    int y = i.y - int(CropTop+0.4);

    bool outsize = x < 0 || x >= width 
                 || y < 0 || y >= height;
                
    
    Result[i.xy] = outsize ? BackgroundColor 
                           : SourceImage[ int2(x,y)];

}

