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

    public Type DataType => typeof(TimeClip);

    [JsonIgnore]
    public float Speed => MathF.Abs(TimeRange.Duration) < 0.001f ? 1 : SourceRange.Duration / TimeRange.Duration;

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
    }
    #endregion

    public bool Assign(IOutputData outputData)
    {
        if (outputData is TimeClip otherTimeClip)
        {
            TimeRange = otherTimeClip.TimeRange;
            SourceRange = otherTimeClip.SourceRange;
            LayerIndex = otherTimeClip.LayerIndex;

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
                       LayerIndex = this.LayerIndex
                   };
    }
}