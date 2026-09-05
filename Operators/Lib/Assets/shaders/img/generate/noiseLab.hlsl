// This shader is heavily based on a ShaderToy Project by CandyCat https://www.shadertoy.com/view/4sc3z2

cbuffer ParamConstants : register(b0)
{
    float4 ColorA;
    float4 ColorB;

    float2 Offset;
    float2 Stretch;

    float Scale;
    float Evolution;
    float Bias;
    float Iterations;

    float3 WarpOffset;

    float Method;
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

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

float IsBetween(float value, float low, float high)
{
    return (value >= low && value <= high) ? 1 : 0;
}

// from https://www.shadertoy.com/view/4djSRW
#define MOD3 float3(.1031, .11369, .13787)

float3 hash33(float3 p3)
{
    p3 = frac(p3 * MOD3);
    p3 += dot(p3, p3.yxz + 19.19);
    return -1.0 + 2.0 * frac(float3((p3.x + p3.y) * p3.z, (p3.x + p3.z) * p3.y, (p3.y + p3.z) * p3.x));
}

float simplex_noise(float3 p)
{
    const float K1 = 0.333333333;
    const float K2 = 0.166666667;

    float3 i = floor(p + (p.x + p.y + p.z) * K1);
    float3 d0 = p - (i - (i.x + i.y + i.z) * K2);

    // thx nikita: https://www.shadertoy.com/view/XsX3zB
    float3 e = step(float3(0, 0, 0), d0 - d0.yzx);
    float3 i1 = e * (1.0 - e.zxy, 1.0 - e.zxy, 1.0 - e.zxy);
    float3 i2 = 1.0 - e.zxy * (1.0 - e);

    float3 d1 = d0 - (i1 - 1.0 * K2);
    float3 d2 = d0 - (i2 - 2.0 * K2);
    float3 d3 = d0 - (1.0 - 3.0 * K2);

    float4 h = max(0.6 - float4(dot(d0, d0), dot(d1, d1), dot(d2, d2), dot(d3, d3)), 0.0);
    float4 n = h * h * h * h * float4(dot(d0, hash33(i)), dot(d1, hash33(i + i1)), dot(d2, hash33(i + i2)), dot(d3, hash33(i + 1.0)));

    return dot(float4(31.316, 31.316, 31.316, 31.316), n);
}

float noise_sum_abs(float3 p)
{
    float f = 0.0;
    p = p * 1.0;
    f += 1.0000 * abs(simplex_noise(p));
    p = 2.0 * p;
    f += 0.5000 * abs(simplex_noise(p));
    p = 2.0 * p;
    f += 0.2500 * abs(simplex_noise(p));
    p = 2.0 * p;
    f += 0.1250 * abs(simplex_noise(p));
    p = 2.0 * p;
    f += 0.0625 * abs(simplex_noise(p));
    p = 2.0 * p;
    return f;
}

// CC0 license https://creativecommons.org/share-your-work/public-domain/cc0/
// From: https://www.shadertoy.com/view/ttdGR8
////////////////// K.jpg's Smooth Re-oriented 8-Point BCC Noise //////////////////
//////////////////// Output: float4(dF/dx, dF/dy, dF/dz, value) ////////////////////

// Borrowed from Stefan Gustavson's noise code
float4 permute(float4 t)
{
    return t * (t * 34.0 + 133.0);
}

#define mod(x, y) ((x) - (y)*floor((x) / (y)))

// Gradient set is a normalized expanded rhombic dodecahedron
float3 grad(float hash)
{

    // Random vertex of a cube, +/- 1 each
    float3 cube = mod(floor(hash / float3(1.0, 2.0, 4.0)), 2.0) * 2.0 - 1.0;

    // Random edge of the three edges connected to that vertex
    // Also a cuboctahedral vertex
    // And corresponds to the face of its dual, the rhombic dodecahedron

    // cuboct[] = 0.0;  // Original glsl code produced warning
    float3 cuboct = cube;
    int index = int(hash / 16.0);
    switch (index)
    {
    case 0:
        cuboct.x = 0.0;
        break;
    case 1:
        cuboct.y = 0.0;
        break;
    case 2:
        cuboct.z = 0.0;
        break;
    }

    // In a funky way, pick one of the four points on the rhombic face
    float type = mod(floor(hash / 8.0), 2.0);
    float3 rhomb = (1.0 - type) * cube + type * (cuboct + cross(cube, cuboct));

    // Expand it so that the new edges are the same length
    // as the existing ones
    float3 grad = cuboct * 1.22474487139 + rhomb;

    // To make all gradients the same length, we only need to shorten the
    // second type of vector. We also put in the whole noise scale constant.
    // The compiler should reduce it into the existing floats. I think.
    grad *= (1.0 - 0.042942436724648037 * type) * 3.5946317686139184;

    return grad;
}

// BCC lattice split up into 2 cube lattices
float4 bccNoiseDerivativesPart(float3 X)
{
    float3 b = floor(X);
    float4 i4 = float4(X - b, 2.5);

    // Pick between each pair of oppposite corners in the cube.
    float3 v1 = b + floor(dot(i4, float4(0.25, 0.25, 0.25, 0.25)));
    float3 v2 = b + float3(1, 0, 0) + float3(-1, 1, 1) * floor(dot(i4, float4(-.25, .25, .25, .35)));
    float3 v3 = b + float3(0, 1, 0) + float3(1, -1, 1) * floor(dot(i4, float4(.25, -.25, .25, .35)));
    float3 v4 = b + float3(0, 0, 1) + float3(1, 1, -1) * floor(dot(i4, float4(.25, .25, -.25, .35)));

    // Gradient hashes for the four vertices in this half-lattice.
    float4 hashes = permute(mod(float4(v1.x, v2.x, v3.x, v4.x), 289.0));
    hashes = permute(mod(hashes + float4(v1.y, v2.y, v3.y, v4.y), 289.0));
    hashes = mod(permute(mod(hashes + float4(v1.z, v2.z, v3.z, v4.z), 289.0)), 48.0);

    // Gradient extrapolations & kernel function
    float3 d1 = X - v1;
    float3 d2 = X - v2;
    float3 d3 = X - v3;
    float3 d4 = X - v4;
    float4 a = max(0.75 - float4(dot(d1, d1), dot(d2, d2), dot(d3, d3), dot(d4, d4)), 0.0);
    float4 aa = a * a;
    float4 aaaa = aa * aa;
    float3 g1 = grad(hashes.x);
    float3 g2 = grad(hashes.y);
    float3 g3 = grad(hashes.z);
    float3 g4 = grad(hashes.w);
    float4 extrapolations = float4(dot(d1, g1), dot(d2, g2), dot(d3, g3), dot(d4, g4));

    // Derivatives of the noise
    // float3 derivative = -8.0 * float3x4(d1, d2, d3, d4) * (aa * a * extrapolations) + float3x4(g1, g2, g3, g4) * aaaa;
    // float3 derivative = mul(float3x4(d1, d2, d3, d4) * -8.0, (aa * a * extrapolations)) + mul(float3x4(g1, g2, g3, g4), aaaa);
    float3 derivative = mul((aa * a * extrapolations), float4x3(d1, d2, d3, d4) * -8.0) + mul(aaaa, float4x3(g1, g2, g3, g4));

    // Return it all as a float4
    return float4(derivative, dot(aaaa, extrapolations));
}

// Rotates domain, but preserve shape. Hides grid better in cardinal slices.
// Good for texturing 3D objects with lots of flat parts along cardinal planes.
float4 bccNoiseDerivatives_XYZ(float3 X)
{
    float d23 = 2.0 / 3.0;
    float3 d233 = float3(d23.xxx);
    X = dot(X, d233) - X;

    float4 result = bccNoiseDerivativesPart(X) + bccNoiseDerivativesPart(X + 144.5);

    return float4(dot(result.xyz, d233) - result.xyz, result.w);
}

// Gives X and Y a triangular alignment, and lets Z move up the main diagonal.
// Might be good for terrain, or a time varying X/Y plane. Z repeats.
float4 bccNoiseDerivatives_XYBeforeZ(float3 X)
{
    // Not a skew transform.
    float3x3 orthonormalMap = float3x3(
        0.788675134594813, -0.211324865405187, -0.577350269189626,
        -0.211324865405187, 0.788675134594813, -0.577350269189626,
        0.577350269189626, 0.577350269189626, 0.577350269189626);

    X = mul(X, orthonormalMap);
    float4 result = bccNoiseDerivativesPart(X) + bccNoiseDerivativesPart(X + 144.5);
    // return float4(mul(result.xyz, orthonormalMap), result.w);
    return float4(mul(orthonormalMap, result.xyz), result.w);
}

//////////////////////////////// End noise code ////////////////////////////////

//----------------------------------------------------------------------------------------------------------------

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth / TargetHeight;
    float2 uv = psInput.texCoord;
    uv -= 0.5;
    uv /= Stretch * Scale;
    uv += Offset * float2(-1 / aspectRatio, 1);
    uv.x *= aspectRatio;

    float3 pos = float3(uv, Evolution / 10);

    if (Method < 0.5)
    {
        int steps = clamp(Iterations + 0.5, 1.1, 5.1);

        float f = 0.7;
        float scaleFactor = 1;
        for (int i = 0; i < steps; i++)
        {
            float f1 = noise_sum_abs(pos * scaleFactor + float3(12.4, 3, 0) * i);
            pos += f * WarpOffset;
            f *= sin(f1) / 2 + 0.5;
            f += 0.2;
        }
        f = 2 * f - 1;

        float fBiased = Bias >= 0
                            ? pow(abs(f), Bias + 1)
                            : 1 - pow(clamp(1 - f, 0, 10), -Bias + 1);

        return lerp(ColorA, ColorB, saturate(fBiased));
    }
    else if (Method < 1.5)
    {

        float4 c = bccNoiseDerivatives_XYBeforeZ(float3(uv * 10, Evolution));
        float f = c.a / 2 + 0.5;

        float fBiased = Bias >= 0
                            ? pow(abs(f), Bias + 1)
                            : 1 - pow(clamp(1 - f, 0, 10), -Bias + 1);

        return lerp(ColorA, ColorB, fBiased);
    }
    else
    {
        float4 c = bccNoiseDerivatives_XYBeforeZ(float3(uv * 10, Evolution));
        return float4((c.rgb / 4 + 0.5), 1);
    }

    return float4(1, 1, 1, 1);
}