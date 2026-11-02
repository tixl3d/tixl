#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Audio.Native;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// A mono source placed in the world. It plays through the operator bus like every other source, so
/// the live mix, the meters, the analysis and an export all hear the same signal.
/// </summary>
public sealed class SpatialOperatorAudioStream : OperatorAudioStreamBase
{
    internal static bool TryLoadStream(string filePath, [NotNullWhen(true)] out SpatialOperatorAudioStream? stream)
    {
        stream = null;

        var engine = AudioMixerManager.NativeEngine;
        if (engine == null || !TryLoadSource(filePath, engine, out var source, out var duration, out var channels, out var sampleRate, spatial: true))
            return false;

        stream = new SpatialOperatorAudioStream(engine, source, duration, filePath);
        stream.ApplySpatial();
        Log.Gated.Audio($"[SpatialAudio] Loaded: '{Path.GetFileName(filePath)}' ({channels}ch, {sampleRate}Hz, {duration:F2}s)");
        return true;
    }

    internal void Update3DPosition(Vector3 position, float minDistance, float maxDistance)
    {
        _velocity = (position - _position) * AssumedFramesPerSecond;
        _position = position;
        _minDistance = Math.Max(0.1f, minDistance);
        _maxDistance = Math.Max(_minDistance + 0.1f, maxDistance);
        ApplySpatial();
    }

    internal void Set3DOrientation(Vector3 orientation)
    {
        _orientation = Vector3.Normalize(orientation);
        ApplySpatial();
    }

    internal void Set3DCone(float innerAngleDegrees, float outerAngleDegrees, float outerVolume)
    {
        _innerAngleDegrees = Math.Clamp(innerAngleDegrees, 0f, 360f);
        _outerAngleDegrees = Math.Clamp(outerAngleDegrees, 0f, 360f);
        _outerVolume = Math.Clamp(outerVolume, 0f, 1f);
        ApplySpatial();
    }

    internal void Set3DMode(NativeAudioEngine.SpatialMode mode)
    {
        _mode = mode;
        ApplySpatial();
    }

    private void ApplySpatial()
    {
        _engine.SetSpatial(Source, _position, _orientation, _velocity, _minDistance, _maxDistance,
                           AudioConfig.RolloffFactor, AudioConfig.DopplerFactor,
                           _innerAngleDegrees, _outerAngleDegrees, _outerVolume, _mode);
    }

    private SpatialOperatorAudioStream(NativeAudioEngine engine, AudioHandle source, double duration, string filePath)
        : base(engine, source, duration, filePath)
    {
        IsStaleStopped = true;
    }

    private const float AssumedFramesPerSecond = 60f;

    private Vector3 _position = Vector3.Zero;
    private Vector3 _velocity = Vector3.Zero;
    private Vector3 _orientation = new(0, 0, 1);
    private float _minDistance = 1f;
    private float _maxDistance = 100f;
    private float _innerAngleDegrees = 360f;
    private float _outerAngleDegrees = 360f;
    private float _outerVolume = 1f;
    private NativeAudioEngine.SpatialMode _mode = NativeAudioEngine.SpatialMode.World;
}
