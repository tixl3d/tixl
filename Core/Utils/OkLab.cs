using System;
using T3.Core.Logging;

namespace T3.Core.Utils;

/**
 * Helper methods to deal with OkLab color space. This can be useful for nicer blending of gradients
 */
public static class OkLab
{
    // From Linear to oklab
    public static Vector4 RgbAToOkLab(Vector4 c)
    {
        double cr = c.X;
        double cg = c.Y;
        double cb = c.Z;

        double l = 0.4122214708d * cr + 0.5363325363d * cg + 0.0514459929d * cb;
        double m = 0.2119034982d * cr + 0.6806995451d * cg + 0.1073969566d * cb;
        double s = 0.0883024619d * cr + 0.2817188376d * cg + 0.6299787005d * cb;

        double lCbrt = Math.Pow (l, 1.0d / 3.0d);
        double mCbrt = Math.Pow (m, 1.0d / 3.0d);
        double sCbrt = Math.Pow (s, 1.0d / 3.0d);

        return new Vector4(
                   (float)(0.2104542553d * lCbrt + 0.793617785d * mCbrt - 0.0040720468d * sCbrt),
                   (float)(1.9779984951d * lCbrt - 2.428592205d * mCbrt + 0.4505937099d * sCbrt),
                           (float)(0.0259040371d * lCbrt + 0.7827717662d * mCbrt - 0.808675766d * sCbrt), 
                                   c.W);        
    }

    // From OKLab to Linear sRGB
    public static Vector4 OkLabToRgba(Vector4 c)
    {
        var l1 = c.X + 0.3963377774f * c.Y + 0.2158037573f * c.Z;
        var m1 = c.X - 0.1055613458f * c.Y - 0.0638541728f * c.Z;
        var s1 = c.X - 0.0894841775f * c.Y - 1.2914855480f * c.Z;

        var l = l1 * l1 * l1;
        var m = m1 * m1 * m1;
        var s = s1 * s1 * s1;

        return new Vector4(
                           +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                           -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                           -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s,
                           c.W
                          );
    }

    public static Vector4 Degamma(Vector4 c)
    {
        const float gamma = 2.2f;
        return new Vector4(
                           MathF.Pow(c.X, gamma),
                           MathF.Pow(c.Y, gamma),
                           MathF.Pow(c.Z, gamma),
                           c.W
                          );
    }
        
    public static Vector4 ToGamma(Vector4 c)
    {
        const float gamma = 2.2f;
        return new Vector4(
                           MathF.Pow(c.X, 1.0f/gamma),
                           MathF.Pow(c.Y, 1.0f/gamma),
                           MathF.Pow(c.Z, 1.0f/gamma),
                           c.W
                          );
    }        
        
    public static Vector4 Mix(Vector4 c1, Vector4 c2, float t)
    {
        var c1Linear = Degamma( Vector4.Max(c1, Vector4.Zero));
        var c2Linear = Degamma(Vector4.Max(c2, Vector4.Zero));
                
        var labMix= MathUtils.Lerp( RgbAToOkLab(c1Linear), RgbAToOkLab(c2Linear), t);
        return ToGamma(OkLab.OkLabToRgba(labMix));
    }
    
    
    
    public static Vector4 FromOkLab(float L, float a, float b, float alpha = 1f, bool gamma = true)
    {
        var hdrExcess = MathF.Max(0f,L - 1f);
        var linear = OkLabToRgba(new Vector4(L.Clamp(0,1), a, b, alpha));
        var clampedLinear = Vector4.Clamp(linear, Vector4.Zero, Vector4.One);
        var srgb  = ToGamma(clampedLinear);
        if(hdrExcess <= 0)
            return srgb;

        return new Vector4(srgb.X * (1 + hdrExcess),
                           srgb.Y * (1 + hdrExcess),
                           srgb.Z * (1 + hdrExcess),
                           srgb.W
                          );
    }

    public static Vector4 FromOkLCh(float L, float C, float hDegrees, float alpha = 1f, bool gamma = true)
    {
        float h = hDegrees * (MathF.PI / 180f);
        float a = C * MathF.Cos(h);
        float b = C * MathF.Sin(h);
        return FromOkLab(L, a, b, alpha, gamma);
    }    
}