using System;

namespace Lib.geometry;

/// <summary>
/// A ScalarField of 3D gradient (Perlin) noise sampled at the field position.
/// Octaves are summed as fBm and normalized, so the output stays roughly within
/// -Amplitude..Amplitude and is centered at zero - ready for displacement.
/// </summary>
[Guid("b3e267ad-4c71-45e9-9a05-e9e1d3f6c882")]
internal sealed class NoiseField : Instance<NoiseField>
{
    [Output(Guid = "7d7af99a-05cc-4e5d-9b12-3e2b8f6a1c47")]
    public readonly Slot<ScalarField> Result = new();

    public NoiseField()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var frequency = Frequency.GetValue(context);
        var octaves = Math.Clamp(Octaves.GetValue(context), 1, 8);
        var amplitude = Amplitude.GetValue(context);
        var offset = Offset.GetValue(context);
        var seed = Seed.GetValue(context);

        // Normalize the octave sum so the range is independent of the octave count
        var totalWeight = 0f;
        var weight = 1f;
        for (var octave = 0; octave < octaves; octave++)
        {
            totalWeight += weight;
            weight *= 0.5f;
        }

        var normalize = amplitude / totalWeight;

        Result.Value = new ScalarField((in FieldSample sample) =>
                                       {
                                           var p = (sample.Position + offset) * frequency;
                                           var sum = 0f;
                                           var octaveWeight = 1f;
                                           for (var octave = 0; octave < octaves; octave++)
                                           {
                                               sum += GradientNoise(p, seed + octave * 131) * octaveWeight;
                                               p *= 2f;
                                               octaveWeight *= 0.5f;
                                           }

                                           return sum * normalize;
                                       });
    }

    private static float GradientNoise(Vector3 p, int seed)
    {
        var ix = (int)MathF.Floor(p.X);
        var iy = (int)MathF.Floor(p.Y);
        var iz = (int)MathF.Floor(p.Z);
        var fx = p.X - ix;
        var fy = p.Y - iy;
        var fz = p.Z - iz;

        var u = Fade(fx);
        var v = Fade(fy);
        var w = Fade(fz);

        var c000 = Grad(Hash(ix, iy, iz, seed), fx, fy, fz);
        var c100 = Grad(Hash(ix + 1, iy, iz, seed), fx - 1, fy, fz);
        var c010 = Grad(Hash(ix, iy + 1, iz, seed), fx, fy - 1, fz);
        var c110 = Grad(Hash(ix + 1, iy + 1, iz, seed), fx - 1, fy - 1, fz);
        var c001 = Grad(Hash(ix, iy, iz + 1, seed), fx, fy, fz - 1);
        var c101 = Grad(Hash(ix + 1, iy, iz + 1, seed), fx - 1, fy, fz - 1);
        var c011 = Grad(Hash(ix, iy + 1, iz + 1, seed), fx, fy - 1, fz - 1);
        var c111 = Grad(Hash(ix + 1, iy + 1, iz + 1, seed), fx - 1, fy - 1, fz - 1);

        var x00 = c000 + (c100 - c000) * u;
        var x10 = c010 + (c110 - c010) * u;
        var x01 = c001 + (c101 - c001) * u;
        var x11 = c011 + (c111 - c011) * u;
        var y0 = x00 + (x10 - x00) * v;
        var y1 = x01 + (x11 - x01) * v;
        return y0 + (y1 - y0) * w;
    }

    private static float Fade(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private static float Grad(int hash, float x, float y, float z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h == 12 || h == 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static int Hash(int x, int y, int z, int seed)
    {
        var h = x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ seed * 285119;
        h = (h ^ (h >> 13)) * 1274126177;
        return h ^ (h >> 16);
    }

    [Input(Guid = "c5f19a44-8b0e-4d63-a7d8-2f6b3e9c0a51")]
    public readonly InputSlot<float> Frequency = new();

    [Input(Guid = "e8a2d1b7-3c94-4f28-b6e0-9d5c7a41f382")]
    public readonly InputSlot<int> Octaves = new();

    [Input(Guid = "1b6e4f92-a8d3-45c7-9e21-c4b8d0f7a635")]
    public readonly InputSlot<float> Amplitude = new();

    [Input(Guid = "9f3c8e15-d762-4b09-a4f8-6e1a2c5d9b74")]
    public readonly InputSlot<Vector3> Offset = new();

    [Input(Guid = "4d91b7c3-6f28-4e5a-8c07-b3f9e6a1d258")]
    public readonly InputSlot<int> Seed = new();
}
