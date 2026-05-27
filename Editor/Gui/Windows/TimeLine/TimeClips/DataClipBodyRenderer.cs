#nullable enable

using ImGuiNET;
using T3.Core.DataTypes.DataSet;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Extra rendering applied to TimeClip op bodies whose output is a <see cref="DataClip"/>:
/// per-event tick marks across the clip body, so the user can see at a glance what
/// events are recorded and roughly where they fall.
/// </summary>
/// <remarks>
/// Hooked from <see cref="TimeClipItem"/> after the body fill and before the label, so
/// the ticks paint over the background colour but under the clip name. Cheap when the
/// op isn't a DataClip producer — a single type check returns early.
/// </remarks>
internal static class DataClipBodyRenderer
{
    /// <summary>
    /// If <paramref name="instance"/> exposes a <see cref="DataClip"/> output with a
    /// non-null value, draws per-event ticks inside the clip body rectangle. No-ops for
    /// any other op type (so it's safe to call unconditionally from the generic clip
    /// renderer).
    /// </summary>
    public static void TryDraw(Instance instance,
                               Vector2 bodyMin,
                               Vector2 bodyMax,
                               ImDrawListPtr drawList)
    {
        DataClip? clip = null;
        foreach (var slot in instance.Outputs)
        {
            if (slot is Slot<DataClip?> dataSlot)
            {
                clip = dataSlot.Value;
                break;
            }
        }

        if (clip?.Set == null || clip.Mapping is not { } mapping)
            return;

        var bodyWidth = bodyMax.X - bodyMin.X;
        if (bodyWidth < 3)
            return;

        // Total event count gates "individual ticks" vs "density block". The threshold is
        // a soft heuristic — at very high density per-event ticks would alias to a solid
        // smear and 30 Hz CC streams could ship thousands of ticks per channel.
        var totalEvents = 0;
        foreach (var channel in clip.Set.Channels)
            totalEvents += channel.Events.Count;

        if (totalEvents == 0)
            return;

        var minY = bodyMin.Y + 3;
        var maxY = bodyMax.Y - 3;
        if (maxY <= minY)
            return;

        // Density fallback: one faded fill across the audible region instead of N ticks
        // when individual ticks would alias. The audible region is mapping.TimeRange
        // (we draw the fill regardless of how far the user has scrolled, clipped by the
        // body rect).
        if (totalEvents > bodyWidth * 0.3f && totalEvents > 200)
        {
            drawList.AddRectFilled(new Vector2(bodyMin.X, minY),
                                   new Vector2(bodyMax.X, maxY),
                                   UiColors.ForegroundFull.Fade(0.18f));
            return;
        }

        // Individual ticks. Use AddLine — single primitive per event, no allocations in
        // the hot path.
        var tickColor = UiColors.ForegroundFull.Fade(0.55f);
        foreach (var channel in clip.Set.Channels)
        {
            foreach (var ev in channel.Events)
            {
                if (ev == null)
                    continue;

                var localBars = mapping.SourceSecsToLocalBars(ev.Time);
                if (localBars < mapping.TimeRange.Start || localBars > mapping.TimeRange.End)
                    continue;

                // World bars → screen X via the TimeRange's pixel span. We don't have a
                // TimeCanvas reference here (the renderer is canvas-agnostic), so derive
                // the X from the proportion within the body rect.
                var t = (localBars - mapping.TimeRange.Start) /
                        (mapping.TimeRange.End - mapping.TimeRange.Start);
                var x = (float)(bodyMin.X + t * bodyWidth);

                drawList.AddLine(new Vector2(x, minY), new Vector2(x, maxY), tickColor);
            }
        }
    }
}
