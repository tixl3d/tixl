cbuffer ParamConstants : register(b0)
{
    float2 Offset;
    float iTime;
    float Scale;
    float AspectRatio;
    float ColorScale;
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
sampler texSampler : register(s0);


// Taken https://www.shadertoy.com/view/4df3Rn

// Converted to HLSL by ChatGPT
// Original GLSL shader by Inigo Quilez
// https://iquilezles.org


float mandelbrot(float2 c)
{
    #if 1
    {
        float c2 = dot(c, c);
        // Skip computation inside M1 - https://iquilezles.org/articles/mset1bulb
        if (256.0 * c2 * c2 - 96.0 * c2 + 32.0 * c.x - 3.0 < 0.0) return 0.0;
        // Skip computation inside M2 - https://iquilezles.org/articles/mset2bulb
        if (16.0 * (c2 + 2.0 * c.x + 1.0) - 1.0 < 0.0) return 0.0;
    }
    #endif

    const float B = 256;
    float l = 0.0;
    float2 z = float2(0.0, 0.0);
    for (int i = 0; i < 512; i++) 
    {
        z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
        if (dot(z, z) > (B * B)) break;
        l += 1.0;
    }

    if (l > 511.0) return 0.0;

    // ------------------------------------------------------
    // Smooth iteration count

    // Equivalent optimized smooth iteration count
    float sl = l - log2(log2(dot(z, z))) + 4.0;
    return sl;
}


float4 psMain(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;
    float2 p = uv;
    float3 col = float3(0.0, 0.0, 0.0);

    p -= 0.5;
    p.y *= -1.0;
    p.x *= AspectRatio;

    p /= pow(10, Scale);
    p+=Offset;

    float f = mandelbrot(p);

    f/=ColorScale;
    return float4(f.xxx,1);
}
