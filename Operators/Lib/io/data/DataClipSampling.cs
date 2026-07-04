#nullable enable

using T3.Core.DataTypes.DataSet;
using T3CoreDataClip = T3.Core.DataTypes.DataSet.DataClip;

namespace Lib.io.data;

/// <summary>
/// Shared logic for the <c>Sample*FromDataClip</c> operators: channel resolution with
/// per-instance caching, playhead→source time mapping, and event sampling. Kept out of
/// the op classes so the float and gate samplers can't drift apart.
/// </summary>
internal static class DataClipSampling
{
    /// <summary>
    /// Resolves <paramref name="channelPath"/> (segments joined with '/') to a channel of
    /// the clip's DataSet. The resolved channel is cached in the caller's fields and only
    /// re-searched when the set instance or the path string changes, keeping the per-frame
    /// path allocation-free.
    /// </summary>
    public static bool TryResolveChannel(T3CoreDataClip? clip, string? channelPath,
                                         ref DataChannel? resolvedChannel, ref DataSet? resolvedSet, ref string? resolvedPath,
                                         out string errorMessage)
    {
        var dataSet = clip?.Set;
        if (dataSet == null)
        {
            resolvedChannel = null;
            resolvedSet = null;
            errorMessage = "No DataClip connected.";
            return false;
        }

        if (string.IsNullOrEmpty(channelPath))
        {
            resolvedChannel = null;
            resolvedSet = dataSet;
            errorMessage = "No channel selected.";
            return false;
        }

        if (resolvedChannel != null && ReferenceEquals(resolvedSet, dataSet) && resolvedPath == channelPath)
        {
            errorMessage = string.Empty;
            return true;
        }

        resolvedSet = dataSet;
        resolvedPath = channelPath;
        resolvedChannel = null;

        foreach (var channel in dataSet.Channels)
        {
            if (MatchesJoinedPath(channel, channelPath))
            {
                resolvedChannel = channel;
                errorMessage = string.Empty;
                return true;
            }
        }

        errorMessage = $"Channel '{channelPath}' not found in clip ({dataSet.Channels.Count} channels).";
        return false;
    }

    /// <summary>
    /// The sample time in source seconds — the unit channel events are stored in. Defaults
    /// to the playhead mapped through the clip's mapping; clips without one (untimed
    /// sources) fall back to interpreting the playhead directly as source time.
    /// </summary>
    public static double GetSampleTime(EvaluationContext context, T3CoreDataClip clip, bool useOverride, float overrideTime)
    {
        if (useOverride)
            return overrideTime;

        if (clip.Mapping is { } mapping)
            return mapping.LocalBarsToSourceSecs(context.LocalTime);

        return context.LocalTime * 240.0 / context.Playback.Bpm;
    }

    /// <summary>Value of the last event at or before <paramref name="time"/>. False when the first event is still ahead.</summary>
    public static bool TrySampleLastValueAtTime(DataChannel channel, double time, out float value)
    {
        value = 0;
        var index = channel.FindIndexForTime(time);
        if (index < 0)
            return false;

        var dataEvent = channel.Events[index];
        if (dataEvent == null || dataEvent.Time > time)
            return false;

        if (!dataEvent.TryGetNumericValue(out var numeric))
            return false;

        value = (float)numeric;
        return true;
    }

    /// <summary>
    /// Whether an interval event covers <paramref name="time"/>, and its value (e.g. note
    /// velocity). Only the last-started interval is checked — recorded and imported MIDI
    /// channels never overlap because a retrigger finishes the previous interval.
    /// </summary>
    public static bool TrySampleActiveInterval(DataChannel channel, double time, out float value)
    {
        value = 0;
        var index = channel.FindIndexForTime(time);
        if (index < 0)
            return false;

        if (channel.Events[index] is not DataIntervalEvent interval
            || interval.Time > time
            || interval.EndTime <= time)
        {
            return false;
        }

        if (interval.TryGetNumericValue(out var numeric))
            value = (float)numeric;

        return true;
    }

    /// <summary>Dropdown options for a channel-select input: all channel paths of the connected clip.</summary>
    public static IEnumerable<string> GetChannelOptions(Guid queriedInputId, Guid channelInputId, T3CoreDataClip? clip)
    {
        if (queriedInputId != channelInputId || clip?.Set == null)
        {
            yield return string.Empty;
            yield break;
        }

        foreach (var channel in clip.Set.Channels)
        {
            yield return string.Join(PathSeparator, channel.Path);
        }
    }

    /// <summary>
    /// Frame-coherent event-start edge detection for a WasHit output: true on the frame
    /// where the sample time crosses an event's start, independent of the event's value —
    /// distinguishes back-to-back events with identical velocity, which pure value
    /// sampling cannot.
    /// </summary>
    /// <remarks>
    /// With several outputs sharing one Update, the method can run more than once per
    /// frame; <paramref name="frameTime"/> (a per-frame constant like
    /// <see cref="T3.Core.Animation.Playback.RunTimeInSecs"/>) keys re-entrant calls to
    /// the cached result so the edge isn't consumed by the second output's pull.
    /// Rewinds reset the edge without firing.
    /// </remarks>
    public struct HitDetector
    {
        public bool Update(double frameTime, double sampleTime, DataChannel channel)
        {
            if (_initialized && frameTime == _lastFrameTime)
                return _lastHit;

            _lastFrameTime = frameTime;

            var hit = false;
            if (!_initialized)
            {
                _initialized = true;
            }
            else if (sampleTime > _previousSampleTime)
            {
                var index = channel.FindIndexForTime(sampleTime);
                if (index >= 0
                    && channel.Events[index] is { } lastStarted
                    && lastStarted.Time <= sampleTime
                    && lastStarted.Time > _previousSampleTime)
                {
                    hit = true;
                }
            }

            _previousSampleTime = sampleTime;
            _lastHit = hit;
            return hit;
        }

        private double _lastFrameTime;
        private double _previousSampleTime;
        private bool _lastHit;
        private bool _initialized;
    }

    private static bool MatchesJoinedPath(DataChannel channel, string joinedPath)
    {
        var offset = 0;
        for (var segmentIndex = 0; segmentIndex < channel.Path.Count; segmentIndex++)
        {
            if (segmentIndex > 0)
            {
                if (offset >= joinedPath.Length || joinedPath[offset] != PathSeparator)
                    return false;
                offset++;
            }

            var segment = channel.Path[segmentIndex];
            if (offset + segment.Length > joinedPath.Length
                || string.CompareOrdinal(joinedPath, offset, segment, 0, segment.Length) != 0)
            {
                return false;
            }

            offset += segment.Length;
        }

        return offset == joinedPath.Length;
    }

    private const char PathSeparator = '/';
}
