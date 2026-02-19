// A collection of various distribution functions
#ifndef BIAS_FUNCTIONS_H
#define BIAS_FUNCTIONS_H
//---- Scalar -------------------------------------

inline float GetBias(float bias, float x)
{
    return x / ((1 / bias - 2) * (1 - x) + 1);
}

inline float GetSchlickBias(float g, float x)
{
    if (x < 0.5)
    {
        x *= 2.0;
        x = 0.5 * GetBias(g, x);
    }
    else
    {
        x = 2.0 * x - 1.0;
        x = 0.5 * GetBias(1.0 - g, x) + 0.5;
    }
    return x;
}

inline float ApplyGainAndBias(float value, float2 gainBias)
{
    float g = saturate(gainBias.x);
    float b = saturate(gainBias.y);

    if (value > 0.999)
        return 1;

    if (value < 0.00001)
        return 0;

    if (g < 0.5)
    {
        value = GetBias(b, value);
        value = GetSchlickBias(g, value);
    }
    else
    {
        value = GetSchlickBias(g, value);
        value = GetBias(b, value);
    }

    return value;
}

//---- float 4 ------------------------------------
inline float4 GetBias(float bias, float4 x)
{
    return x / ((1 / bias - 2) * (1 - x) + 1);
}

inline float4 GetSchlickBias(float4 x, float gain)
{
    return x < 0.5 ? GetBias(gain, x * 2.0) / 2.0
                   : GetBias(1.0 - gain, x * 2.0 - 1.0) / 2.0 + 0.5;
}

inline float4 ApplyGainAndBias(float4 v4, float2 gainBias)
{
    float g = saturate(gainBias.x);
    float b = saturate(gainBias.y);

    // avoid modifying 0 and 1 for extreme bias and gain values
    float4 hiMask = step(0.999f, v4);
    float4 loMask = step(v4, 0.00001f);
    float4 result = v4;

    if (g < 0.5)
    {
        v4 = GetBias(b, v4);
        v4 = GetSchlickBias(v4, g);
    }
    else
    {
        v4 = GetSchlickBias(v4, g);
        v4 = GetBias(b, v4);
    }

    // Replace values >= 0.999f with 1
    result = hiMask * 1.0f + (1.0f - hiMask) * result;

    // Replace values <= 0.00001f with 0
    result = (1.0f - loMask) * result; // since loMask * 0 is always 0
    return v4;
}

// We used this originally, but although faster and functionally complete
// modifying bias with gain 0.5 has is neutral which is an unexpected user
// behaviour.

// // based on: https://arxiv.org/pdf/2010.09714.pdf
// // but s remapped from [-64 .. 64] -> [0 .. 1]
float ApplyBiasAndGain(float x, float s, float t)
{
    float eps = 0.0001;
    float r = 200;
    s *= 2;
    s = s < 1 ? (pow(r, 1 - s)) : 1 / pow(r, s - 1);
    return x < t
               ? ((t * x) / (x + s * (t - x) + eps))
               : (((1 - t) * (x - 1)) / (1 - x - s * (t - x) + eps) + 1);
}

float4 ApplyBiasAndGain(float4 x, float s, float t)
{
    float eps = 0.0001;
    float r = 200;
    s *= 2;
    s = s < 1 ? (pow(r, 1 - s)) : 1 / pow(r, s - 1);
    return x < t
               ? ((t * x) / (x + s * (t - x) + eps))
               : (((1 - t) * (x - 1)) / (1 - x - s * (t - x) + eps) + 1);
}

#endif