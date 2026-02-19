// A shader to combine three images into the R,G,B,A channels of a new image.
// Thomas Helzle - Screendream.de 2022 

cbuffer ParamConstants : register(b0)
{
    float4 ImageAColor;
    float4 ImageBColor;
    float4 ImageCColor;
    float Select_R;
    float Select_G;
    float Select_B;
    float AlphaMode;
}

// cbuffer TimeConstants : register(b1)
// {
//     float globalTime;
//     float time;
//     float runTime;
//     float beatTime;
// }

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
Texture2D<float4> ImageB : register(t1);
Texture2D<float4> ImageC : register(t2);
sampler texSampler : register(s0);


float IsBetween( float value, float low, float high) {
    return (value >= low && value <= high) ? 1:0;
}


float4 psMain(vsOutput psInput) : SV_TARGET
{    
    float4 tA = ImageA.Sample(texSampler, psInput.texCoord) * ImageAColor; 
    float4 tB = ImageB.Sample(texSampler, psInput.texCoord) * ImageBColor;
    float4 tC = ImageC.Sample(texSampler, psInput.texCoord) * ImageCColor;     

    float a = 0;    

    switch( (int)AlphaMode) {

        case 0:
            a = tA.a;
            break;
            
        case 1:
            a = tB.a;
            break;
            
        case  2:
            a = tC.a;
            break;

        case 3:
            a = 0.0;
            break;

        case 4:
            a = 1.0;            
            break;
    }

    int3 selects= int3((int)Select_R,(int)Select_G,(int)Select_B);
    float v=0;
    float4 color = 0;
    for(int i = 0;i<3; i++) 
    {
        switch( selects[i]) {
            // R
            case 0:  v = tA.r; break;
            // G
            case 1:  v = tA.g; break;
            // B
            case 2:  v = tA.b; break;
            // Average
            case 3:  v = (tA.r + tA.g + tA.b) / 3.0; break;
            // Brightness
            case 4:  v = min(1.0, max(0.0, 0.239 * tA.r + 0.686 * tA.g + 0.075 * tA.b)); break;
            // R
            case 5:  v = tB.r; break;
            // G
            case 6:  v = tB.g; break;
            // B
            case 7:  v = tB.b; break;
            // Average
            case 8:  v = (tB.r + tB.g + tB.b) / 3.0; break;
            // Brightness
            case 9:  v = min(1.0, max(0.0, 0.239 * tB.r + 0.686 * tB.g + 0.075 * tB.b)); break;
            // R
            case 10:  v = tC.r; break;
            // G
            case 11:  v = tC.g; break;
            // B
            case 12:  v = tC.b; break;
            // Average
            case 13:  v = (tC.r + tC.g + tC.b) / 3.0; break;
            // Brightness
            case 14:  v = min(1.0, max(0.0, 0.239 * tC.r + 0.686 * tC.g + 0.075 * tC.b)); break;
        }
        color[i] =v;
    }
    return float4(color.rgb, a);        
}
