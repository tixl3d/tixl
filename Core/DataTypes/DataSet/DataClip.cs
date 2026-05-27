#nullable enable

using T3.Core.Animation;

namespace T3.Core.DataTypes.DataSet;

/// <summary>
/// A <see cref="T3.Core.DataTypes.DataSet.DataSet"/> placed on the timeline — the value
/// type ops carry on their graph wires when they need to expose recorded MIDI / OSC /
/// data both as content and as a TimeClip with a mapped playhead. Parallels the
/// DAW concept of a clip wrapping a re-usable piece of content with its placement.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DataSet"/> itself stays meaningful without a clip — ops that operate on
/// raw content (mean of a channel, energy sum, etc.) consume <see cref="DataSet"/>
/// directly. <see cref="DataClip"/> only adds the timeline-mapping context that
/// real-time replay ops (<c>SimulateIoData</c>) and timeline-aware analysis ops need.
/// </para>
/// <para>
/// <see cref="Mapping"/> is nullable so untimed sources can still wrap their content
/// in a <see cref="DataClip"/> if they want a uniform output type — but consumers that
/// need the mapping must check for null and skip the frame.
/// </para>
/// </remarks>
public sealed class DataClip
{
    public required DataSet Set { get; init; }

    /// <summary>How the clip is placed on the timeline. Null = no timeline context.</summary>
    public TimeRangeMapping? Mapping { get; init; }
}
