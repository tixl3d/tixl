using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using T3.Core.Animation;

namespace T3.Core.Utils;

[SuppressMessage("ReSharper", "UnusedMember.Local")]
public static class MathUtils
{
    public static float ToRad => (float)(Math.PI / 180.0);
    public static float ToDegree => (float)(180.0 / Math.PI);

    public static float PerlinNoise(float value, float period, int octaves, int seed)
    {
        var noiseSum = 0.0f;
        octaves = octaves.Clamp(1, 20);
        var frequency = period;
        var amplitude = 0.5f;
        for (var octave = 0; octave < octaves - 1; octave++)
        {
            var v = value * frequency + seed * 12.468f;
            var a = Noise((int)v, seed);
            var b = Noise((int)v + 1, seed);
            var t = Fade(v - (float)Math.Floor(v));
            noiseSum += Lerp(a, b, t) * amplitude;
            frequency *= 2;
            amplitude *= 0.5f;
        }

        return noiseSum;
    }

    private static float Noise(int x, int seed)
    {
        int n = x + seed * 137;
        n = (n << 13) ^ n;
        return (float)(1.0 - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetBias(float b, float x)
    {
        return x / (((1f / b - 2f) * (1f - x)) + 1f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetSchlickBias(float g, float x)
    {
        if (x < 0.5f)
        {
            x *= 2f;
            x = 0.5f * GetBias(g, x);
        }
        else
        {
            x = 2f * x - 1f;
            x = 0.5f * GetBias(1f - g, x) + 0.5f;
        }
        return x;
    }
    
    public static float ApplyGainAndBias(this float value, float gain, float bias)
    {
        var b = bias.Clamp(0,1);
        var g = gain.Clamp(0,1);

        if (value > 0.999f)
            return 1f;

        if (value < 0.00001f)
            return 0f;

        if (g < 0.5f)
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
    
    
    
    
    [Obsolete("Please use ApplyGainAndBias()")]
    public static float ApplyBiasAndGain(this float value, float gain,float bias )
    {
        bias = Clamp(bias, 0.001f, 0.999f);
        gain = Clamp(gain, 0.001f, 0.999f);
    
        // Apply bias
        value /= ((1.0f / bias - 2.0f) * (1.0f - value) + 1.0f);
        
        var gainFactorLow = 1.0f / gain - 2.0f;
        var gainFactorHigh = 1.0f / (1.0f - gain) - 2.0f;
    
        // Use conditional expression to determine scaled value
        var scaledValue = value < 0.5f
                              ? (value * 2.0f) / (gainFactorLow * (1.0f - value * 2.0f) + 1.0f) * 0.5f
                              : ((value * 2.0f - 1.0f) / (gainFactorHigh * (1.0f - (value * 2.0f - 1.0f)) + 1.0f)) * 0.5f + 0.5f;
        return scaledValue;
    }
        
        
    public static uint XxHash(uint p)
    {
        const uint prime32A = 3266489917U;
        const uint prime32B = 668265263U, prime32C = 374761393U;

        uint h32 = p + prime32C;
        h32 = prime32B * ((h32 << 17) | (h32 >> (32 - 17)));
        h32 = 2246822519U * (h32 ^ (h32 >> 15));
        h32 = prime32A * (h32 ^ (h32 >> 13));

        return h32 ^ (h32 >> 16);
    }
    
    public static int XxHash(int p)
    {
        return (int)XxHash((uint)p);
    }
        
    public static float Hash01( uint x )
    {
        x *= 13331U;
        const uint k = 1103515245U;  // GLIB C
        x = ((x>>8)^x)*k;
        x = ((x>>8)^x)*k;
    
        return (float)( (x & 0x7fffffff) / 2147483648.0);
    }

    private static float Fade(float t)
    {
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    public static float SmootherStep(float min, float max, float value)
    {
        var t = Math.Max(0, Math.Min(1, (value - min) / (max - min)));
        return Fade(t);
    }

    private static float SmoothStep(float min, float max, float value)
    {
        var x = Math.Max(0, Math.Min(1, (value - min) / (max - min)));
        return x * x * (3 - 2 * x);
    }

    private static double SmoothStep(double min, double max, double value)
    {
        var x = Math.Max(0, Math.Min(1, (value - min) / (max - min)));
        return x * x * (3 - 2 * x);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToDegrees(this float val)
    {
        return val * 180 / MathF.PI;
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToRadians(this float val)
    {
        return val * MathF.PI / 180;
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool _IsFinite(this float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }        
        
    public static bool _IsFinite(this Vector3 value)
    {
        return value.X._IsFinite() && value.Y._IsFinite() && value.Z._IsFinite();
    }        
        
    public static Vector2 Clamp(this Vector2 v, Vector2 mn, Vector2 mx)
    {
        return new Vector2((v.X < mn.X)? mn.X : (v.X > mx.X) ? mx.X : v.X, 
                           (v.Y < mn.Y) ? mn.Y : (v.Y > mx.Y) ? mx.Y : v.Y);
    }

    public static Vector3 Clamp(this Vector3 v, Vector3 mn, Vector3 mx)
    {
        return new Vector3((v.X < mn.X)? mn.X : (v.X > mx.X) ? mx.X : v.X, 
                           (v.Y < mn.Y) ? mn.Y : (v.Y > mx.Y) ? mx.Y : v.Y,
                           (v.Z < mn.Z) ? mn.Z : (v.Z > mx.Z) ? mx.Z : v.Z);
    }

    public static Vector2 Remap(this Vector2 value2, Vector2 inMin, Vector2 inMax, Vector2 outMin, Vector2 outMax)
    {
        var factor = (value2 - inMin) / (inMax - inMin);
        var v = factor * (outMax - outMin) + outMin;
        return v;
    }    
    
    public static Vector3 Remap(this Vector3 value2, Vector3 inMin, Vector3 inMax, Vector3 outMin, Vector3 outMax)
    {
        var factor = (value2 - inMin) / (inMax - inMin);
        var v = factor * (outMax - outMin) + outMin;
        return v;
    }    
    
    // TODO: move to another class
    public static int FindIndexForTime<T>(List<T> items, double time, Func<int, double> timeAtIndex)
    {
        if (items.Count == 0)
            return -1;
            
        var lastIndex = items.Count - 1;
        var firstIndex = 0;
            
        if (timeAtIndex(lastIndex) <= time)
            return lastIndex;
            
        if (timeAtIndex(firstIndex) >= time)
            return firstIndex;
            
        while (lastIndex - firstIndex > 1)
        {
            var middleIndex = (firstIndex + lastIndex) / 2;
                
            var delta = timeAtIndex(middleIndex) - time;
                
            if (delta < 0)
                firstIndex = middleIndex;
            else
                lastIndex = middleIndex;
        }
        return firstIndex;
    }
        
    // [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // public static T Min<T>(T a, T b) where T : INumber<T>
    //     => T.Min(a, b);
    //
    // [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // public static T Max<T>(T a, T b) where T : INumber<T>
    //     => T.Max(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Clamp<T>(this T v, T min, T max) where T : INumber<T>
        => T.Min(T.Max(v, min), max);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ClampMin<T>(this T v, T min) where T : INumber<T>
        => T.Max(v, min);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ClampMax<T>(this T v, T max) where T : INumber<T>
        => T.Min(v, max);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T OptionalClamp<T>(T v, T min, bool clampMin, T max, bool clampMax) where T : INumber<T>
    {
        var r = v;
        if (clampMin) r = T.Max(r, min);
        if (clampMax) r = T.Min(r, max);
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Mod(this int val, int repeat)
    {
        // Prevent exception
        if(repeat == 0)
            return 0;
            
        var x = val % repeat;
        if (x < 0)
            x = repeat + x;
            
        return x;
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float[] ToArray(this Vector2 vec2)
    {
        return [vec2.X, vec2.Y];
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float[] ToArray(this Vector3 vec3)
    {
        return [vec3.X, vec3.Y, vec3.Z];
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float[] ToArray(this Vector4 vec4)
    {
        return [vec4.X, vec4.Y, vec4.Z, vec4.W];
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LerpRadianAngle(float from, float to, float t)
    {
        var delta = Fmod((from - to), 2* MathF.PI);
        if (delta > MathF.PI)
            delta -= 2* MathF.PI;
            
        return from - delta * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LerpDegreesAngle(float from, float to, float t)
    {
        var delta = Fmod((from - to), 360);
        if (delta > 180)
            delta -= 360;
            
        return from - delta * t;
    }

    
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Fmod(float v, float mod)
    {
        return v - mod * (float)Math.Floor(v / mod);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Fmod(double v, double mod)
    {
        return v - mod * Math.Floor(v / mod);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NormalizeAndClamp(float value, float min, float max)
    {
        return MathF.Max(0, MathF.Min(1,(value - min) / (max - min)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double NormalizeAndClamp(double value, double min, double max)
    {
        return Math.Max(0, Math.Min(1,(value - min) / (max - min)));
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float RemapAndClamp(this float value, float inMin, float inMax, float outMin, float outMax)
    {
        var factor = (value - inMin) / (inMax - inMin);
        var v = factor * (outMax - outMin) + outMin;
        if (outMin > outMax)
            Utilities.Swap(ref outMin, ref outMax);
        return v.Clamp(outMin, outMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Remap(this float value, float inMin, float inMax, float outMin, float outMax)
    {
        var factor = (value - inMin) / (inMax - inMin);
        var v = factor * (outMax - outMin) + outMin;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Remap(this double value, double inMin, double inMax, double outMin, double outMax)
    {
        var factor = (value - inMin) / (inMax - inMin);
        var v = factor * (outMax - outMin) + outMin;
        return v;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double RemapAndClamp(double value, double inMin, double inMax, double outMin, double outMax)
    {
        var factor = (value - inMin) / (inMax - inMin);
        var v = factor * (outMax - outMin) + outMin;
        if (v > outMax)
        {
            v = outMax;
        }
        else if (v < outMin)
        {
            v = outMin;
        }

        return v;
    }

    public static Vector2 Min(Vector2 lhs, Vector2 rhs)
    {
        return new Vector2(lhs.X < rhs.X ? lhs.X : rhs.X, lhs.Y < rhs.Y ? lhs.Y : rhs.Y);
    }

    public static Vector2 Floor(this Vector2 v)
    {
        return new Vector2((float)Math.Floor(v.X), (float)Math.Floor(v.Y));
    }

    public static Vector2 Max(Vector2 lhs, Vector2 rhs)
    {
        return new Vector2(lhs.X >= rhs.X ? lhs.X : rhs.X, lhs.Y >= rhs.Y ? lhs.Y : rhs.Y);
    }

    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        return new Vector2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return new Vector3(a.X + (b.X - a.X) * t,
                           a.Y + (b.Y - a.Y) * t,
                           a.Z + (b.Z - a.Z) * t);
    }

    public static Vector4 Lerp(Vector4 a, Vector4 b, float t)
    {
        return new Vector4(a.X + (b.X - a.X) * t,
                           a.Y + (b.Y - a.Y) * t,
                           a.Z + (b.Z - a.Z) * t,
                           a.W + (b.W - a.W) * t);
    }

    public static double Lerp(double a, double b, double t)
    {
        return a + (b - a) * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Lerp(int a, int b, float t)
    {
        return (int)(a + (b - a) * t);
    }

    /// <summary>
    /// Slowly lerps the parameter in place towards the target. 
    /// </summary>
    /// <returns>Also returns the new parameter for convenience.</returns>
    public static float DampTowards(this ref float value, float target, float damping = 0.9f)
    {
        value = Lerp( target,value, damping);
        if (!value._IsFinite())
        {
            value = 0;
        }
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Log2(double value)
    {
        return Math.Log10(value) / Math.Log10(2.0);
    }

    public static float RoundValue(float i, float stepsPerUnit, float stepRatio)
    {
        float u = 1 / stepsPerUnit;
        float v = stepRatio / (2 * stepsPerUnit);
        float m = i % u;
        float r = m - (m < v
                           ? 0
                           : (m > (u - v))
                               ? u
                               : ((m - v) / (1 - 2 * stepsPerUnit * v)));
        float y = i - r;
        return y;
    }

    /// <summary>
    /// Smooth damps a value with a "critically damped spring" similar to unity's SmoothDamp helper method.
    /// See https://stackoverflow.com/a/5100956 
    /// </summary>
    public static float SpringDamp(float target,
                                   float current,
                                   ref float velocity,
                                   float springConstant = 2,
                                   float timeStep = 1 / 60f)
    {
        //const float springConstant = 0.41f;
        var currentToTarget = target - current;
        var springForce = currentToTarget * springConstant;
        var dampingForce = -velocity * 2 * MathF.Sqrt(springConstant);
        var force = springForce + dampingForce;
        velocity += force * timeStep;
        var displacement = velocity * timeStep;
        return current + displacement;
    }

    public const float Pi2 = (float)Math.PI * 2;

    public static Vector3 ToVector3(this Vector4 vec)
    {
        return new Vector3(vec.X / vec.W, vec.Y / vec.W, vec.Z / vec.W);
    }
        
    /// <summary>
    /// Return true if a boolean changed
    /// </summary>
    public static bool WasChanged(bool newState, ref bool current)
    {
        if (newState == current)
            return false;
            
        current = newState;
        return true;
    }
        
    /// <summary>
    /// Return true if a boolean changed from false to true
    /// </summary>
    public static bool WasTriggered(bool newState, ref bool current)
    {
        if (newState == current)
            return false;

        current = newState;
        return newState;
    }

    /// <summary>
    /// Return true if a boolean changed from false to true
    /// </summary>
    public static bool WasReleased(bool newState, ref bool current)
    {
        if (newState == current)
            return false;

        current = newState;
        return !newState;
    }

    /// <summary>
    /// Checks for NaN or Infinity, and sets the float to the provided default value if either.
    /// </summary>
    /// <returns>True if NaN or Infinity</returns>
    public static bool ApplyDefaultIfInvalid(ref float val, float defaultValue)
    {
        var isInvalid = float.IsNaN(val) || float.IsInfinity(val);
        val = isInvalid ? defaultValue : val;
        return isInvalid;
    }

    public static bool ApplyDefaultIfInvalid(ref Vector2 val, Vector2 defaultValue)
    {
        var isInvalid = float.IsNaN(val.X) || float.IsInfinity(val.X) ||
                        float.IsNaN(val.Y) || float.IsInfinity(val.Y);
        val = isInvalid ? defaultValue : val;
        return isInvalid;
    }
        
    public static bool ApplyDefaultIfInvalid(ref Vector3 val, Vector3 defaultValue)
    {
        var isInvalid = float.IsNaN(val.X) || float.IsInfinity(val.X) ||
                        float.IsNaN(val.Y) || float.IsInfinity(val.Y) ||
                        float.IsNaN(val.Z) || float.IsInfinity(val.Z);
        val = isInvalid ? defaultValue : val;
        return isInvalid;
    }

    /// <summary>
    /// Checks for NaN or Infinity, and sets the double to the provided default value if either.
    /// </summary>
    /// <returns>True if NaN or Infinity</returns>
    public static bool ApplyDefaultIfInvalid(ref double val, double defaultValue)
    {
        bool isInvalid = double.IsNaN(val) || double.IsInfinity(val);
        val = isInvalid ? defaultValue : val;
        return isInvalid;
    }

    public static Quaternion RotationFromTwoPositions(Vector3 p1, Vector3 p2)
    {
        return Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), (float)(Math.Atan2(p1.X - p2.X, -(p1.Y - p2.Y)) + Math.PI / 2));
    }
    
    public static float MaxComponent(this Vector4 vector4)
    {
        return   MathF.Max(
                           MathF.Max(vector4.X, vector4.Y), 
                           MathF.Max(vector4.Z, vector4.W));
    }
    

    public static bool HasHdrRange(Vector4 color, out float intensity)
    {
        var maxColorBrightness = color.MaxComponent();
        if (maxColorBrightness > 1)
        {
            intensity= 1f - MathF.Pow(1.5f, -maxColorBrightness);
            return true;
        }

        intensity = 0;
        return false;
    }

    public static Quaternion LookAt(Vector3 forward, Vector3 up)
    {
        var right = Vector3.Normalize(Vector3.Cross(forward, up));
        up = Vector3.Normalize(Vector3.Cross(forward, right));

        float m00 = right.X;
        float m01 = right.Y;
        float m02 = right.Z;
        float m10 = up.X;
        float m11 = up.Y;
        float m12 = up.Z;
        float m20 = forward.X;
        float m21 = forward.Y;
        float m22 = forward.Z;

        float num8 = (m00 + m11) + m22;
        Quaternion q = Quaternion.Identity;
        if (num8 > 0.0)
        {
            float num = MathF.Sqrt(num8 + 1.0f);
            q.W = num * 0.5f;
            num = 0.5f / num;
            q.X = (m12 - m21) * num;
            q.Y = (m20 - m02) * num;
            q.Z = (m01 - m10) * num;
            return q;
        }

        if ((m00 >= m11) && (m00 >= m22))
        {
            float num7 = MathF.Sqrt(((1.0f + m00) - m11) - m22);
            float num4 = 0.5f / num7;
            q.X = 0.5f * num7;
            q.Y = (m01 + m10) * num4;
            q.Z = (m02 + m20) * num4;
            q.W = (m12 - m21) * num4;
            return q;
        }

        if (m11 > m22)
        {
            float num6 = MathF.Sqrt(((1.0f + m11) - m00) - m22);
            float num3 = 0.5f / num6;
            q.X = (m10 + m01) * num3;
            q.Y = 0.5f * num6;
            q.Z = (m21 + m12) * num3;
            q.W = (m20 - m02) * num3;
            return q;
        }

        float num5 = MathF.Sqrt(((1.0f + m22) - m00) - m11);
        float num2 = 0.5f / num5;
        q.X = (m20 + m02) * num2;
        q.Y = (m21 + m12) * num2;
        q.Z = 0.5f * num5;
        q.W = (m01 - m10) * num2;
        return q;
    }
}

public static class EaseFunctions
{
    public static float EaseOutElastic(float x)
    {
        const float c4 = (float)(2 * Math.PI) / 3;

        return x <= 0f
                   ? 0f
                   : x >= 1f
                       ? 1f
                       : (float)(Math.Pow(2, -10 * x) * Math.Sin((x * 10 - 0.75) * c4) + 1);
    }
}

public static class DampFunctions
{
    public enum Methods
    {
        LinearInterpolation,
        DampedSpring
    }

    public static float DampenFloat(float inputValue, float previousValue, float damping, ref float velocity, Methods method)
    {
        return method switch
                   {
                       Methods.LinearInterpolation => LinearDamp(inputValue, previousValue, damping),
                       Methods.DampedSpring        => SpringDampFloat(inputValue, previousValue, damping, ref velocity),
                       _                           => inputValue
                   };
    }

    public static float SpringDampFloat(float inputValue, float previousValue, float damping, ref float velocity)
    {
        return MathUtils.SpringDamp(inputValue, previousValue, ref velocity, 0.5f / (damping + 0.001f), (float)Playback.LastFrameDuration);
    }

    private static float LinearDamp(float targetValue, float currentValue, float damping)
    {
        // TODO: Fix damping factor from framerate 
        return MathUtils.Lerp(targetValue, currentValue, damping);
    }
        
    public static Vector2 SpringDampVec2(Vector2 targetVec, Vector2 currentValue, float damping, ref Vector2 velocity)
    {
        var dt = (float)Playback.LastFrameDuration;
        return new Vector2(
                           MathUtils.SpringDamp(targetVec.X, currentValue.X, ref velocity.X, 0.5f / (damping + 0.001f), dt),
                           MathUtils.SpringDamp(targetVec.Y, currentValue.Y, ref velocity.Y, 0.5f / (damping + 0.001f), dt));
    }

    public static Vector3 SpringDampVec3(Vector3 targetVec, Vector3 currentValue, float damping, ref Vector3 velocity)
    {
        var dt = (float)Playback.LastFrameDuration;
        return new Vector3(
                           MathUtils.SpringDamp(targetVec.X, currentValue.X, ref velocity.X, 0.5f / (damping + 0.001f), dt),
                           MathUtils.SpringDamp(targetVec.Y, currentValue.Y, ref velocity.Y, 0.5f / (damping + 0.001f), dt),
                           MathUtils.SpringDamp(targetVec.Z, currentValue.Z, ref velocity.Z, 0.5f / (damping + 0.001f), dt));
    }


}