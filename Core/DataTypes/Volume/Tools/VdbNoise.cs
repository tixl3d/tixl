#nullable enable
using System;
using System.Numerics;

namespace T3.Core.DataTypes.Volume;

public enum VdbNoiseType
{
    Perlin,
    Simplex,
    Worley,
}

public enum VdbNoiseFractalType
{
    None,
    Fbm,
    Ridged,
    Billow,
}

/// <summary>
/// Deterministic 3D noise (Perlin, simplex, Worley) with fractal combining (fBm, ridged, billow),
/// the procedural workhorse behind [VolumeNoise]. Seeded via a permutation-table offset so any
/// number of independent fields can coexist.
/// </summary>
public sealed class VdbNoise
{
    private readonly byte[] _perm = new byte[512];

    public VdbNoise(int seed = 0)
    {
        var perm = new byte[256];
        for (var i = 0; i < 256; i++)
            perm[i] = (byte)i;

        var rng = new Random(seed);
        for (var i = 255; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (perm[i], perm[j]) = (perm[j], perm[i]);
        }

        for (var i = 0; i < 512; i++)
            _perm[i] = perm[i & 255];
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float GradHash(int hash, float x, float y, float z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    /// <summary>Classic Perlin gradient noise in [-1, 1].</summary>
    public float Perlin(float x, float y, float z)
    {
        var xi = (int)MathF.Floor(x) & 255;
        var yi = (int)MathF.Floor(y) & 255;
        var zi = (int)MathF.Floor(z) & 255;

        var xf = x - MathF.Floor(x);
        var yf = y - MathF.Floor(y);
        var zf = z - MathF.Floor(z);

        var u = Fade(xf);
        var v = Fade(yf);
        var w = Fade(zf);

        var p = _perm;

        var aaa = p[p[p[xi] + yi] + zi];
        var aba = p[p[p[xi] + yi + 1] + zi];
        var aab = p[p[p[xi] + yi] + zi + 1];
        var abb = p[p[p[xi] + yi + 1] + zi + 1];
        var baa = p[p[p[xi + 1] + yi] + zi];
        var bba = p[p[p[xi + 1] + yi + 1] + zi];
        var bab = p[p[p[xi + 1] + yi] + zi + 1];
        var bbb = p[p[p[xi + 1] + yi + 1] + zi + 1];

        var x1 = Lerp(GradHash(aaa, xf, yf, zf), GradHash(baa, xf - 1, yf, zf), u);
        var x2 = Lerp(GradHash(aba, xf, yf - 1, zf), GradHash(bba, xf - 1, yf - 1, zf), u);
        var y1 = Lerp(x1, x2, v);

        var x3 = Lerp(GradHash(aab, xf, yf, zf - 1), GradHash(bab, xf - 1, yf, zf - 1), u);
        var x4 = Lerp(GradHash(abb, xf, yf - 1, zf - 1), GradHash(bbb, xf - 1, yf - 1, zf - 1), u);
        var y2 = Lerp(x3, x4, v);

        return Lerp(y1, y2, w); // ~[-1, 1]
    }

    /// <summary>OpenSimplex-style gradient noise in [-1, 1]; less axis-aligned than Perlin.</summary>
    public float Simplex(float x, float y, float z)
    {
        const float skew = 1.0f / 3.0f;
        const float unskew = 1.0f / 6.0f;

        var s = (x + y + z) * skew;
        var i = (int)MathF.Floor(x + s);
        var j = (int)MathF.Floor(y + s);
        var k = (int)MathF.Floor(z + s);

        var t = (i + j + k) * unskew;
        var x0 = x - (i - t);
        var y0 = y - (j - t);
        var z0 = z - (k - t);

        int i1, j1, k1, i2, j2, k2;
        if (x0 >= y0)
        {
            if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
            else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
        }
        else
        {
            if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
            else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
            else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
        }

        var x1 = x0 - i1 + unskew;
        var y1 = y0 - j1 + unskew;
        var z1 = z0 - k1 + unskew;
        var x2 = x0 - i2 + 2 * unskew;
        var y2 = y0 - j2 + 2 * unskew;
        var z2 = z0 - k2 + 2 * unskew;
        var x3 = x0 - 1 + 3 * unskew;
        var y3 = y0 - 1 + 3 * unskew;
        var z3 = z0 - 1 + 3 * unskew;

        var ii = i & 255;
        var jj = j & 255;
        var kk = k & 255;
        var p = _perm;

        var n0 = SimplexCorner(p[p[p[ii] + jj] + kk], x0, y0, z0);
        var n1 = SimplexCorner(p[p[p[ii + i1] + jj + j1] + kk + k1], x1, y1, z1);
        var n2 = SimplexCorner(p[p[p[ii + i2] + jj + j2] + kk + k2], x2, y2, z2);
        var n3 = SimplexCorner(p[p[p[ii + 1] + jj + 1] + kk + 1], x3, y3, z3);

        return 32.0f * (n0 + n1 + n2 + n3); // ~[-1, 1]
    }

    private static float SimplexCorner(int hash, float x, float y, float z)
    {
        var t = 0.6f - x * x - y * y - z * z;
        if (t < 0)
            return 0;

        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        t *= t;
        return t * t * (GradHash(hash, x, y, z));
        // v unused in some branches matches the classic reference implementation.
    }

    /// <summary>Worley/cellular noise: distance (in cells) to the nearest feature point in [0, 1].</summary>
    public float Worley(float x, float y, float z)
    {
        var xi = (int)MathF.Floor(x);
        var yi = (int)MathF.Floor(y);
        var zi = (int)MathF.Floor(z);

        var minDist = float.MaxValue;
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var cx = xi + dx;
                    var cy = yi + dy;
                    var cz = zi + dz;
                    var h = _perm[(_perm[(_perm[(cx & 255)] + (cy & 255)) & 511] + (cz & 255)) & 511];
                    var px = cx + (h & 7) / 8.0f + 0.0625f * ((h >> 4) & 1);
                    var py = cy + ((h >> 1) & 7) / 8.0f + 0.0625f * ((h >> 5) & 1);
                    var pz = cz + ((h >> 2) & 7) / 8.0f + 0.0625f * ((h >> 6) & 1);
                    var d = (px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z);
                    if (d < minDist)
                        minDist = d;
                }
            }
        }

        return MathF.Min(MathF.Sqrt(minDist), 1.5f);
    }

    public float Single(VdbNoiseType type, float x, float y, float z) => type switch
    {
        VdbNoiseType.Perlin => Perlin(x, y, z),
        VdbNoiseType.Simplex => Simplex(x, y, z),
        VdbNoiseType.Worley => Worley(x, y, z),
        _ => 0f,
    };

    /// <summary>
    /// Fractal noise: <paramref name="octaves"/> layers at doubling frequency and halving gain.
    /// Fbm sums, ridged folds at 1-|n| (creases), billow folds at |2n|-1 (puffy). Result ~[-1, 1]
    /// (worley ~[0, 1]).
    /// </summary>
    public float Fractal(VdbNoiseType type, VdbNoiseFractalType fractal, float x, float y, float z,
                         int octaves, float lacunarity = 2.0f, float gain = 0.5f)
    {
        octaves = Math.Clamp(octaves, 1, 12);
        var sum = 0f;
        var amplitude = 1f;
        var frequency = 1f;
        var normalization = 0f;

        for (var o = 0; o < octaves; o++)
        {
            var n = Single(type, x * frequency, y * frequency, z * frequency);
            switch (fractal)
            {
                case VdbNoiseFractalType.Fbm:
                    sum += n * amplitude;
                    break;
                case VdbNoiseFractalType.Ridged:
                    sum += (1 - MathF.Abs(n)) * amplitude * 2 - 1;
                    break;
                case VdbNoiseFractalType.Billow:
                    sum += (MathF.Abs(n) * 2 - 1) * amplitude;
                    break;
                default:
                    sum = n;
                    break;
            }

            normalization += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return normalization > 0 ? sum / normalization : sum;
    }
}
