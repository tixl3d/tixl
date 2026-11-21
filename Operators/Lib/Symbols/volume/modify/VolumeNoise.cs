using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using Lib.Utils;

namespace Lib.volume.modify;

/// Adds procedural fractal noise (Perlin, Simplex or Worley with fBm/Ridged/Billow combination) to a volume. Add/Multiply modulate values; Displace pushes level-set surfaces along the noise for organic shapes.
[Guid("157fac50-f3ea-5404-94ef-89b470fd6194")]
internal sealed class VolumeNoise : Instance<VolumeNoise>
{
    private readonly record struct Key(VdbVolume Volume, int Seed, VdbNoiseType NoiseType, VdbNoiseFractalType Fractal, int Octaves,
                                       float Frequency, float Amplitude, Vector3 Offset, T3.Core.DataTypes.Volume.Tools.VolumeNoise.Mode Mode);

    [Output(Guid = "2f675093-d490-50ea-ac15-613f7c5b367d")]
    public readonly Slot<VdbVolume> Result = new();

    private enum NoiseTypes
    {
        Perlin,
        Simplex,
        Worley,
    }

    private enum FractalTypes
    {
        None,
        Fbm,
        Ridged,
        Billow,
    }

    private enum Modes
    {
        Add,
        Multiply,
        Displace,
        Replace,
    }

    public VolumeNoise()
    {
        Result.UpdateAction = Update;
        _job = new BackgroundJob<Key, VdbVolume>(this, nameof(VolumeNoise), Compute, Result);
    }

    private void Update(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        var seed = Seed.GetValue(context);
        var noiseType = (VdbNoiseType)NoiseType.GetValue(context);
        var fractal = (VdbNoiseFractalType)Fractal.GetValue(context);
        var octaves = Math.Clamp(Octaves.GetValue(context), 1, 8);
        var frequency = Frequency.GetValue(context);
        var amplitude = Amplitude.GetValue(context);
        var offset = Offset.GetValue(context);
        var mode = (T3.Core.DataTypes.Volume.Tools.VolumeNoise.Mode)Mode.GetValue(context);

        var grid = volume?.GetFloatGrid();
        if (grid == null)
        {
            _job.Reset();
            Result.Value = volume;
            return;
        }

        if (!float.IsFinite(frequency) || !float.IsFinite(amplitude) || !float.IsFinite(offset.X + offset.Y + offset.Z))
        {
            _job.Reset();
            Result.Value = null;
            return;
        }

        // Noise evaluates octaves per active voxel; an animated Offset therefore shows the newest finished
        // result each frame instead of stalling playback until every frame's noise is done.
        var key = new Key(volume, seed, noiseType, fractal, octaves, frequency, amplitude, offset, mode);
        switch (_job.Poll(context, key, out var result))
        {
            case BackgroundJobOutcome.Ready:
                Result.Value = result;
                break;
            case BackgroundJobOutcome.Failed:
                Result.Value = null;
                break;
        }
    }

    private static VdbVolume Compute(Key key)
    {
        var noise = new VdbNoise(key.Seed);
        return new VdbVolume(T3.Core.DataTypes.Volume.Tools.VolumeNoise.Apply(key.Volume.GetFloatGrid(), noise, key.Frequency, key.Amplitude,
                                                                              key.Offset, key.Octaves, key.NoiseType, key.Fractal, key.Mode));
    }

    private readonly BackgroundJob<Key, VdbVolume> _job;

    [Input(Guid = "20af828b-f18e-5822-95e1-e9708aa8fd01")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "435f4c7b-b598-5cb0-a4d5-5875652c6518")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "f7e2ced5-9031-57ec-9be4-30252ca644a1", MappedType = typeof(NoiseTypes))]
    public readonly InputSlot<int> NoiseType = new();

    [Input(Guid = "1ee7f2e0-d46f-5bd6-909e-1cd0cb15d0d0", MappedType = typeof(FractalTypes))]
    public readonly InputSlot<int> Fractal = new();

    [Input(Guid = "3af327f6-9030-57a5-bd59-949410d3275e")]
    public readonly InputSlot<int> Octaves = new();

    [Input(Guid = "255e7244-b30b-5a05-ad44-cb5512d39f40")]
    public readonly InputSlot<float> Frequency = new();

    [Input(Guid = "fa3b21cd-383f-5f82-9b5a-57c316cdef7d")]
    public readonly InputSlot<float> Amplitude = new();

    [Input(Guid = "a8c2bbd9-1601-59c4-bbdb-db3189a7bdf7")]
    public readonly InputSlot<Vector3> Offset = new();

    [Input(Guid = "3d95a0ca-fd17-53e5-817f-fdfe293f3b6e", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

}