#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Serialization;

namespace T3.Core.Animation;

/// <summary>Unit a clip time range is expressed in. Timeline placement is always bars; source ranges may be either.</summary>
public enum ClipTimeUnits
{
    Bars,
    Seconds,
}

/// <summary>
/// Maps are timeline region to a source time region and contains some additional attributes for display in timeline editor.
/// </summary>
public sealed class TimeClip : IOutputData
{
    // Used when creating new timeClips
    // ReSharper disable once MemberCanBePrivate.Global
    public TimeClip()
    {
        var t = Playback.Current != null
                    ? (float)Playback.Current.TimeInBars
                    : 0;
        TimeRange = new TimeRange(t, t + DefaultClipDuration);
        SourceRange = new TimeRange(t, t + DefaultClipDuration);
    }

    private const float DefaultClipDuration = 4f;
    public Guid Id { get; set; }
    public TimeRange TimeRange;
    public TimeRange SourceRange;
    public int LayerIndex { get; set; } = 0;

    /// <summary>
    /// TimeClips that primary purpose is use with nested content with remapping local time to that
    /// content time. Operators like TimeClipSwitch can clear this flag to indicate that the source
    /// region should be linked to the clip region when dragging clips in the timeline. 
    /// </summary>
    public bool UsedForRegionMapping = true;

    /// <summary>
    /// Unit of <see cref="SourceRange"/> — and thus of the local time the clip's content and its keyframes run in.
    /// Seconds for wall-clock media (video, audio), so trims and keys stay on the content when the project BPM
    /// changes; Bars for musical content (nested compositions, MIDI). Derived from the owning op's type by
    /// <c>TimeClipSlot</c>; persisted so files written before the unit existed can be recognized and converted.
    /// </summary>
    public ClipTimeUnits SourceUnit;

    /// <summary>
    /// True for a clip read from a file that predates <see cref="SourceUnit"/>: its source values are in bars
    /// whatever the op type. <c>TimeClipSlot</c> converts them once the op type (and thus the unit) is known.
    /// </summary>
    [JsonIgnore]
    public bool NeedsSourceUnitConversion { get; internal set; }

    /// <summary>Converts a time in the clip's source space to seconds of the source media.</summary>
    public double SourceToSeconds(double sourceTime, double playbackBpm)
        => SourceUnit == ClipTimeUnits.Seconds ? sourceTime : sourceTime * 240.0 / playbackBpm;

    public double SecondsToSource(double seconds, double playbackBpm)
        => SourceUnit == ClipTimeUnits.Seconds ? seconds : seconds * playbackBpm / 240.0;

    public Type DataType => typeof(TimeClip);

    /// <summary>Source units per timeline bar — the affine rate of the clip's time mapping.</summary>
    [JsonIgnore]
    public float Speed => MathF.Abs(TimeRange.Duration) < 0.001f ? 1 : SourceRange.Duration / TimeRange.Duration;

    /// <summary>Real-time playback rate of the content (1 = native speed).</summary>
    public double GetPlaybackSpeed(double playbackBpm)
        => SourceUnit == ClipTimeUnits.Seconds ? Speed * playbackBpm / 240.0 : Speed;

    /// <summary>
    /// Maps a time on the parent timeline (bars) into the clip's source time (bars).
    /// Affine and unbounded — callers clamp or gate as needed.
    /// </summary>
    public double MapTimelineToSource(double timelineBars)
    {
        var pos = timelineBars - TimeRange.Start;
        var duration = TimeRange.End - TimeRange.Start;
        if (Math.Abs(duration) > 0.0001f)
            pos *= (SourceRange.End - SourceRange.Start) / duration;

        return pos + SourceRange.Start;
    }

    /// <summary>
    /// Inverse of <see cref="MapTimelineToSource"/>: maps a source time (bars) to its position on the
    /// parent timeline. Used to place source-time data (e.g. keyframes) at the playback time it takes effect.
    /// </summary>
    public double MapSourceToTimeline(double sourceBars)
    {
        var pos = sourceBars - SourceRange.Start;
        var sourceDuration = SourceRange.End - SourceRange.Start;
        if (Math.Abs(sourceDuration) > 0.0001f)
            pos *= (TimeRange.End - TimeRange.Start) / sourceDuration;

        return pos + TimeRange.Start;
    }

    public bool MakeConform()
    {
        var neededFix = false;
        
        if (!TimeRange.Start._IsFinite())
        {
            TimeRange.Start = 0;
            neededFix = true;
        }

        if (!TimeRange.End._IsFinite())
        {
            TimeRange.End = TimeRange.Start + DefaultClipDuration;
            neededFix = true;
        }
        
        if (!SourceRange.Start._IsFinite())
        {
            SourceRange.Start = TimeRange.Start;
            neededFix = true;
        }

        if (!SourceRange.End._IsFinite())
        {
            SourceRange.End = TimeRange.End;
            neededFix = true;
        }
        
        return neededFix;
    }

    public bool IsClipOverlappingOthers(IEnumerable<TimeClip> allTimeClips)
    {
        foreach (var otherClip in allTimeClips)
        {
            if (otherClip == this)
                continue;

            if (LayerIndex != otherClip.LayerIndex)
                continue;

            var start = TimeRange.Start;
            var end = TimeRange.End;
            var otherStart = otherClip.TimeRange.Start;
            var otherEnd = otherClip.TimeRange.End;

            if (otherEnd <= start || otherStart >= end)
                continue;

            return true;
        }

        return false;
    }

    #region serialization
    public void ToJson(JsonTextWriter writer)
    {
        writer.WritePropertyName("TimeClip");
        writer.WriteStartObject();
        writer.WritePropertyName("TimeRange");
        writer.WriteStartObject();
        writer.WriteValue("Start", TimeRange.Start);
        writer.WriteValue("End", TimeRange.End);
        writer.WriteEndObject();
        writer.WritePropertyName("SourceRange");
        writer.WriteStartObject();
        writer.WriteValue("Start", SourceRange.Start);
        writer.WriteValue("End", SourceRange.End);
        writer.WriteEndObject();
        writer.WriteValue("LayerIndex", LayerIndex);
        writer.WriteObject("SourceUnit", SourceUnit);
        writer.WriteEndObject();
    }

    public void ReadFromJson(JToken json)
    {
        var timeClip = json["TimeClip"];
        if (timeClip == null)
            return;

        var timeRange = timeClip["TimeRange"];
        if (timeRange != null)
            TimeRange = new TimeRange(timeRange.Value<float>("Start"), timeRange.Value<float>("End"));

        var sourceRange = timeClip["SourceRange"];
        if (sourceRange != null)
            SourceRange = new TimeRange(sourceRange.Value<float>("Start"), sourceRange.Value<float>("End"));

        LayerIndex = timeClip.Value<int>("LayerIndex");

        var unitToken = timeClip["SourceUnit"];
        if (unitToken != null && Enum.TryParse<ClipTimeUnits>(unitToken.Value<string>(), out var unit))
        {
            SourceUnit = unit;
        }
        else
        {
            NeedsSourceUnitConversion = true;
        }
    }
    #endregion

    public bool Assign(IOutputData outputData)
    {
        if (outputData is TimeClip otherTimeClip)
        {
            TimeRange = otherTimeClip.TimeRange;
            SourceRange = otherTimeClip.SourceRange;
            LayerIndex = otherTimeClip.LayerIndex;
            SourceUnit = otherTimeClip.SourceUnit;

            return true;
        }

        Log.Error($"Trying to assign output data of type '{outputData.GetType()}' to 'TimeClip'.");

        return false;
    }

    public TimeClip Clone()
    {
        return new TimeClip
                   {
                       Id = Guid.NewGuid(),
                       TimeRange = this.TimeRange,
                       SourceRange = this.SourceRange,
                       LayerIndex = this.LayerIndex,
                       SourceUnit = this.SourceUnit,
                   };
    }
}