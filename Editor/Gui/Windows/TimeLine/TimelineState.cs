#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

using T3.Core.Animation;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Persisted per-symbol timeline state. Stored under "Timeline" in the .t3ui Settings block.
/// </summary>
internal sealed class TimelineState
{
    // View
    public float ScaleX = 1;
    public float ScrollX;

    // Mode
    [JsonConverter(typeof(StringEnumConverter))]
    public TimeLineCanvas.Modes Mode = TimeLineCanvas.Modes.DopeView;

    /// <summary>
    /// Height of the timeline below the graph, or -1 for the auto-computed height. Unlike the rest
    /// of this class it is project-wide window layout, so only the root symbol's copy is ever
    /// written — see <see cref="TimeLineCanvas.TimelineHeight"/>.
    /// </summary>
    public int TimelineHeight = -1;

    /// <summary>True when the user resized the graph/timeline splitter, so this state is worth saving.</summary>
    internal bool HasCustomHeight => TimelineHeight > 0;

    // Inline DataClip edit area — toggled via the AudioFile icon next to the Record button
    // on the timeline toolbar. When on AND a TimeClip with a DataClip output is selected,
    // the TimelineDetailsArea pane below the dope sheet resolves to clip-editing mode.
    public bool InlineDataClipEditEnabled = false;

    /// <summary>
    /// User-controlled height of the inline details pane (curve editor OR DataClip editor
    /// — both share one pane and one persisted height). Resized via the splitter above
    /// the pane. Default 200 px roughly matches the previous curve area's auto-fit at a
    /// typical timeline height; the splitter clamps to a sensible band so the pane can't
    /// collapse to nothing or eat the whole timeline.
    /// </summary>
    public float DetailsAreaHeight = 200f;

    /// <summary>
    /// Authored span of the symbol's meaningful content in bars ("this transition covers 0..8").
    /// Editor-only metadata consumed when the symbol is used as a time clip: default SourceRange
    /// for new clip instances and footage extent in the parent timeline. Null when never authored —
    /// consumers fall back to a derived range. Stored as two floats so the JSON stays free of
    /// TimeRange's computed properties.
    /// </summary>
    [JsonIgnore]
    public TimeRange? SourceExtent
    {
        get => SourceExtentStart.HasValue && SourceExtentEnd.HasValue
                   ? new TimeRange(SourceExtentStart.Value, SourceExtentEnd.Value)
                   : null;
        set
        {
            SourceExtentStart = value?.Start;
            SourceExtentEnd = value?.End;
        }
    }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public float? SourceExtentStart;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public float? SourceExtentEnd;

    /// <summary>
    /// Back-compat alias for <see cref="DetailsAreaHeight"/>. Older .t3ui files persisted
    /// the per-mode "InlineDataClipStripHeight"; Newtonsoft's deserializer surfaces it
    /// here and we forward the value in <see cref="ReadFromJson"/>. Kept private so
    /// nothing else writes through it.
    /// </summary>
    [JsonProperty(Required = Required.Default)]
    private float? InlineDataClipStripHeight { get; set; }

    #region Serialization

    internal void WriteToJson(JsonTextWriter writer)
    {
        writer.WritePropertyName("Timeline");
        writer.WriteRawValue(JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    internal static TimelineState? ReadFromJson(JToken? settingsToken)
    {
        if (settingsToken == null)
            return null;

        var token = settingsToken["Timeline"];
        var state = token?.ToObject<TimelineState>();
        if (state == null)
            return null;

        // Back-compat: forward the legacy per-mode field onto the unified one when the
        // .t3ui predates the merge. Wins over the default 200 px but loses to any
        // explicit DetailsAreaHeight that's also present in the file.
        if (state.InlineDataClipStripHeight is { } legacy && state.DetailsAreaHeight == 200f)
            state.DetailsAreaHeight = legacy;

        return state;
    }

    #endregion
}
