#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Stats;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.Analyze;

/// <summary>
/// Renders a <see cref="RollingMetric"/> as an overlaid histogram (bars in the background)
/// and plot line (samples over time in the foreground). Pure draw helpers — no layout logic,
/// no internal state. Caller is responsible for allocating rect space (e.g. via <c>ImGui.Dummy</c>).
/// </summary>
internal static class MetricGraphView
{
    /// <summary>
    /// Draws histogram + plot line into <paramref name="rect"/>.
    /// </summary>
    /// <param name="scratch">Reusable array of at least <c>metric.Capacity</c> floats, owned by the caller.</param>
    /// <param name="plotMaxY">Upper bound of the plot-line Y axis. Samples above this clamp to the top.</param>
    /// <param name="flashDurationSec">Histogram bars flash toward <paramref name="flashColor"/> for this long after being incremented.</param>
    /// <summary>
    /// Draws plot line + histogram. Returns the hovered histogram bucket index, or -1 when the
    /// mouse is not over any bucket. Caller uses the return value to render a per-bucket tooltip.
    /// </summary>
    public static int DrawGraph(ImDrawListPtr drawList, ImRect rect,
        RollingMetric metric, float[] scratch,
        Color barColor, Color flashColor, Color lineColor,
        double now, float plotMaxY,
        string? axisLeftLabel = null, string? axisMidLabel = null, string? axisRightLabel = null,
        float flashDurationSec = 0.25f)
    {
        if (metric.Count == 0)
            return -1;

        var size = rect.GetSize();
        if (size.X <= 0 || size.Y <= 0)
            return -1;

        // Layout: plot line (top), histogram (middle), mean-indicator triangle, optional axis labels (bottom).
        const float meanIndicatorHeight = 5f;
        var hasAxisLabels = axisLeftLabel != null || axisMidLabel != null || axisRightLabel != null;
        float axisLabelHeight = 0f;
        if (hasAxisLabels)
        {
            ImGui.PushFont(Fonts.FontSmall);
            axisLabelHeight = ImGui.GetTextLineHeight() + 1f;
            ImGui.PopFont();
        }

        var workingHeight = size.Y - meanIndicatorHeight - axisLabelHeight;
        if (workingHeight < 6f)
            return -1;

        var midY = rect.Min.Y + workingHeight * 0.5f;
        var bottomY = rect.Min.Y + workingHeight;
        var plotRect = new ImRect(rect.Min, new Vector2(rect.Max.X, midY));
        var histoRect = new ImRect(new Vector2(rect.Min.X, midY), new Vector2(rect.Max.X, bottomY));

        // Hover detection runs before the draw so the histogram can highlight the selected range.
        var hoverRect = new ImRect(histoRect.Min, new Vector2(histoRect.Max.X, rect.Max.Y));
        var hoveredBucket = -1;
        if (ImGui.IsMouseHoveringRect(hoverRect.Min, hoverRect.Max))
        {
            var histoBarWidth = histoRect.GetWidth() / metric.BucketCount;
            if (histoBarWidth > 0)
            {
                var idx = (int)((ImGui.GetMousePos().X - histoRect.Min.X) / histoBarWidth);
                if (idx >= 0 && idx < metric.BucketCount)
                    hoveredBucket = idx;
            }
        }

        // Holding Shift while hovering enables cumulative selection (this bucket and everything right of it).
        var highlightFromBucket = hoveredBucket >= 0 && ImGui.GetIO().KeyShift ? hoveredBucket : -1;

        DrawPlotLine(drawList, plotRect, metric, scratch, lineColor, plotMaxY);
        DrawHistogram(drawList, histoRect, metric, now, barColor, flashColor, lineColor, flashDurationSec, highlightFromBucket);

        if (hasAxisLabels)
        {
            ImGui.PushFont(Fonts.FontSmall);
            var labelY = bottomY + meanIndicatorHeight + 1f;
            var labelColor = (uint)UiColors.BackgroundFull.Fade(0.8f);

            if (axisLeftLabel != null)
                drawList.AddText(new Vector2(rect.Min.X, labelY), labelColor, axisLeftLabel);

            if (axisMidLabel != null)
            {
                var midLabelSize = ImGui.CalcTextSize(axisMidLabel);
                drawList.AddText(new Vector2(rect.GetCenter().X - midLabelSize.X * 0.5f, labelY), labelColor, axisMidLabel);
            }

            if (axisRightLabel != null)
            {
                var rightLabelSize = ImGui.CalcTextSize(axisRightLabel);
                drawList.AddText(new Vector2(rect.Max.X - rightLabelSize.X, labelY), labelColor, axisRightLabel);
            }
            ImGui.PopFont();
        }

        return hoveredBucket;
    }

    /// <summary>
    /// Histogram bars + mean-indicator triangle just below the baseline. Bar heights use a log curve
    /// so tall bars don't dominate; empty buckets render as a single transparent line.
    /// When <paramref name="highlightFromBucket"/> is non-negative, buckets at or right of that index
    /// are tinted toward <see cref="UiColors.StatusActivated"/> — used for the Shift-cumulative selection.
    /// </summary>
    private static void DrawHistogram(ImDrawListPtr drawList, ImRect rect, RollingMetric metric, double now,
        Color barColor, Color flashColor, Color meanColor, float flashDurationSec = 1,
        int highlightFromBucket = -1)
    {
        var slots = metric.Slots;
        var bucketCount = slots.Length;
        if (bucketCount == 0)
            return;

        var size = rect.GetSize();
        if (size.X <= 0 || size.Y <= 0)
            return;

        var barWidth = size.X / bucketCount;
        var heightScale = size.Y / LogHeight(metric.Capacity); // full window height == LogHeight(capacity)
        var y1 = rect.Max.Y;

        // Baseline
        drawList.AddLine(new Vector2(rect.Min.X, y1 - 0.5f),
            new Vector2(rect.Max.X, y1 +1),
            UiColors.BackgroundFull.Fade(0.5f));


        for (var i = 0; i < bucketCount; i++)
        {
            var slot = slots[i];
            if (slot.CountRecent == 0)
                continue;

            var x0 = rect.Min.X + i * barWidth;
            var x1 = rect.Min.X + (i + 1) * barWidth - 2f;
            var h = (LogHeight(slot.CountRecent) * heightScale).ClampMin(1);
            var fadeFactor = (float)((now - slot.LastIncrementTime) / flashDurationSec).Clamp(0, 1);
            var color = Color.Mix(flashColor, barColor, fadeFactor);

            if (highlightFromBucket >= 0 && i >= highlightFromBucket)
                color = Color.Mix(color, UiColors.StatusActivated, 0.6f);

            drawList.AddRectFilled(new Vector2(x0, y1 - h), new Vector2(x1, y1), color);
        }

        
        // Mean indicator: small triangle below the histogram, pointing up at the bucket containing the mean.
        var mean = metric.Average;
        var meanBucket = 0;
        for (var i = 0; i < bucketCount; i++)
        {
            if (slots[i].ValueRangeMin <= mean)
                meanBucket = i;
            else
                break;
        }
        var meanX = rect.Min.X + (meanBucket + 0.5f) * barWidth;
        var tipY = y1 + 1f;
        drawList.AddTriangleFilled(
            new Vector2(meanX, tipY),
            new Vector2(meanX + 3f, tipY + 4f),
            new Vector2(meanX - 3f, tipY + 4f),
            meanColor);
    }

    /// <summary>
    /// Plot-line only. Samples are laid out left-to-right in chronological order.
    /// </summary>
    public static void DrawPlotLine(ImDrawListPtr drawList, ImRect rect, RollingMetric metric, float[] scratch,
        Color lineColor, float plotMaxY)
    {
        var samples = metric.AsOrderedSpan(scratch);
        var count = samples.Length;
        if (count < 2)
            return;

        var size = rect.GetSize();
        if (size.X <= 0 || size.Y <= 0)
            return;

        if (plotMaxY <= 0f)
            return;

        var stepX = size.X / (count - 1);
        var baseY = rect.Max.Y;
        var height = size.Y;
        uint color = lineColor;

        var prev = new Vector2(rect.Min.X, baseY - Math.Clamp(samples[0] / plotMaxY, 0f, 1f) * height);
        for (var i = 1; i < count; i++)
        {
            var x = rect.Min.X + i * stepX;
            var y = baseY - Math.Clamp(samples[i] / plotMaxY, 0f, 1f) * height - 3;
            var next = new Vector2(x, y);
            drawList.AddLine(prev, next, color);
            prev = next;
        }

        // Marker at the latest sample.
        drawList.AddCircleFilled(prev, 2f, color);
    }

    /// <summary>Exponentially-compressed bar-height mapping. count=0 → 0, grows as log10(1+count).</summary>
    private static float LogHeight(int count) => count <= 0 ? 0f : MathF.Log10(1 + count) * 15f;
}