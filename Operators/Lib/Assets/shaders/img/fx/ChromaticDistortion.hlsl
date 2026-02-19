//RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> ImageA : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float Size;
    float Colorize;
    float Distort;
    float DistortOffset;
    float ScaleImage;
    float SampleCount;

}


cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};


#define mod(x,y) (x-y*floor(x/y))


float3 chromaShift(float range) {
    return float3(
        clamp( 1.5 - abs(range + 1), 0,1) * 2,
        clamp( 1.5 - abs(range ), 0,1)*1.5,
        clamp( 1.5 - abs(range - 1), 0,1) * 2
    );
}


float3 chromeShiftSine(float range) {
    return float3(
        (sin(( clamp(range,0,0.5)-0.75) * 3.1415 * 2)/ 2+0.5) * 4,
        (sin((range-0.25) * 3.1415 * 2)/ 2+0.5)*2,
        (sin(( clamp(range,0.5,1)-0.75) * 3.1415 * 2)/ 2+0.5)*4
    );
}


// Newerer, happier, unicornsier and rainbowsier version of the above!
// (c) yupferris
// Thanks to Rune Stubbe for some math optimizations here :)
// see https://www.shadertoy.com/view/MdsyDX
float3 chromaShiftLinear(float f)
{
    f = f * 3.0 - 1.5;
    float3 col= saturate(float3(-f, 1.0 - abs(f), f));
    return pow(col, 1.0 / 2.2);
}


float4 psMain(vsOutput psInput) : SV_TARGET
{
    float clampedSampleCount = clamp( ((int)SampleCount/2)*2,1,100);

    float2 p = psInput.texCoord;
    float2 fromCenter = (0.5 + Center - p) / ScaleImage;

    float distance = length(fromCenter);
    float bulge = 1+Distort*0.5;

    fromCenter *= pow(distance * DistortOffset, Distort) * bulge;

    //float2 sampleDir = 0;
    //float radialStrength= Size * pow( length(sampleDir),0.3);
    float2 dir = Size * fromCenter * pow( length(fromCenter),0.3);
    //float2 dir = sampleDir * radialStrength;
    p = 0.5-fromCenter + Center;
    //return float4(fromCenter,0,1);
    //return ImageA.Sample(texSampler, p);

    float4 blurredSum = 0;
    float3 chromarizedSum = 0;
    
    float step = 2./(clampedSampleCount+0.5);
    // float2 p2 = -fromCenter+Center+0.5;
    
    for (float f= -1.0; f <= 1; f+=step)
    {
        float4 col = ImageA.SampleLevel(texSampler, p + dir*f ,0);
        blurredSum += col;
        //chromarizedSum += col.rgb * chromeShiftSine(f/2+0.5);
        //chromarizedSum += col.rgb * chromaShiftLinear(f/2 + 0.5)*2;        
        chromarizedSum += col.rgb * chromaShift(f);
    }
    
    chromarizedSum /= (clampedSampleCount);
    blurredSum /= clampedSampleCount;
    return float4(lerp(blurredSum.rgb, chromarizedSum.rgb, Colorize) , blurredSum.a);
}
