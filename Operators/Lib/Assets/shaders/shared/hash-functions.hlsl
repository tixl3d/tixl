#ifndef HASH_FUNCTIONS_H
#define HASH_FUNCTIONS_H

#define _PRIME0 13331U
#define _PRIME1 1345777U
#define _PRIME2 98777177U

//----------------------------------------------------------------------------------------
//  1 out, 1 in...
float hash11(float p)
{
    p = frac(p * .1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

//----------------------------------------------------------------------------------------
//  1 out, 2 in...
float hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

//----------------------------------------------------------------------------------------
//  1 out, 3 in...
float hash13(float3 p3)
{
    p3 = frac(p3 * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

//----------------------------------------------------------------------------------------
//  2 out, 1 in...
float2 hash21(float p)
{
    float3 p3 = frac(float3(p, p, p) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

//----------------------------------------------------------------------------------------
///  2 out, 2 in...
float2 hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

//----------------------------------------------------------------------------------------
///  2 out, 3 in...
float2 hash23(float3 p3)
{
    p3 = frac(p3 * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

//----------------------------------------------------------------------------------------
//  3 out, 1 in...
float3 hash31(float p)
{
    float3 p3 = frac(float3(p, p, p) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

//----------------------------------------------------------------------------------------
///  3 out, 2 in...
float3 hash32(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

//----------------------------------------------------------------------------------------
///  3 out, 3 in...
float3 hash33(float3 p3)
{
    p3 = frac(p3 * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yxx) * p3.zyx);
}

// 2017 Inigo Quilez
// https://www.shadertoy.com/view/XlXcW4
float3 hash33u(uint3 x)
{
    const uint k = 1103515245U; // GLIB C
    x = ((x >> 8U) ^ x.yzx) * k;
    x = ((x >> 8U) ^ x.yzx) * k;
    x = ((x >> 8U) ^ x.yzx) * k;

    return float3(x) * (1.0 / float(0xffffffffU));
}

float4 hash41u(uint x)
{
    const uint k = 1103515245U; // GLIB C
    x *= _PRIME0;
    x = ((x >> 8U) ^ x) * k;
    uint y = ((x >> 8U) ^ x) * k;
    uint z = ((y >> 8U) ^ x) * k;
    uint w = ((z >> 8U) ^ y) * k;
    uint4 i4 = uint4(x, y, z, w);

    return float4(i4) * (1.0 / float(0xffffffffU));
}

float hash11u(uint x)
{
    const uint k = 1103515245U; // GLIB C
    x *= _PRIME0;
    x = ((x >> 8U) ^ x) * k;
    x = ((x >> 8U) ^ x) * k;

    return float(x) * (1.0 / float(0xffffffffU));
}

//----------------------------------------------------------------------------------------
// 4 out, 1 in...
float4 hash41(float p)
{
    float4 p4 = frac(float4(p, p, p, p) * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

//----------------------------------------------------------------------------------------
// 4 out, 2 in...
float4 hash42(float2 p)
{
    float4 p4 = frac(float4(p.xyxy) * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

//----------------------------------------------------------------------------------------
// 4 out, 3 in...
float4 hash43(float3 p)
{
    float4 p4 = frac(float4(p.xyzx) * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

//----------------------------------------------------------------------------------------
// 4 out, 4 in...
float4 hash44(float4 p4)
{
    p4 = frac(p4 * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

uint pcg(in uint v)
{
    const uint state = v * 747796405u + 2891336453u;
    const uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;

    return (word >> 22u) ^ word;
}

uint xxhash(in uint p)
{
    const uint PRIME32_2 = 2246822519U, PRIME32_3 = 3266489917U;
    const uint PRIME32_4 = 668265263U, PRIME32_5 = 374761393U;

    uint h32 = p + PRIME32_5;
    h32 = PRIME32_4 * ((h32 << 17) | (h32 >> (32 - 17)));
    h32 = PRIME32_2 * (h32 ^ (h32 >> 15));
    h32 = PRIME32_3 * (h32 ^ (h32 >> 13));

    return h32 ^ (h32 >> 16);
}

#endif