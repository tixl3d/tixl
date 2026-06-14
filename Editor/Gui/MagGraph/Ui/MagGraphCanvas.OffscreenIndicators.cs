using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    /// <summary>
    /// When the selection — or an operator highlighted from another panel — lies outside the visible
    /// graph area, draws a thin marker that rides the rounded window border at the off-screen box's
    /// projected position, so the user can find it without changing zoom or pan.
    /// </summary>
    private void DrawOffscreenIndicators(ImDrawListPtr drawList)
    {
        // Selection: steady marker. Iterate the (small) selection list directly rather than the
        // item draw loop, which culls off-screen items before its selection check.
        var selection = _context.Selector.Selection;
        if (selection.Count > 0)
        {
            var hasBounds = false;
            var bounds = new ImRect();
            for (var i = 0; i < selection.Count; i++)
            {
                var s = selection[i];
                AccumulateBounds(ref bounds, ref hasBounds, ImRect.RectWithSize(s.PosOnCanvas, s.Size));
            }

            if (hasBounds && !IsRectVisible(bounds))
                DrawEdgeMarker(drawList, bounds, UiColors.ForegroundFull);
        }

        // Hover: blinking marker for an operator highlighted from another panel. Mirrors the on-node
        // highlight condition (FrameStats hover + graph window not hovered) so it reads the same.
        if (!IsHovered)
        {
            var hoveredIds = FrameStats.Last.HoveredIds;
            if (hoveredIds.Count > 0)
            {
                var hasBounds = false;
                var bounds = new ImRect();
                foreach (var id in hoveredIds)
                {
                    if (_context.Layout.Items.TryGetValue(id, out var item))
                        AccumulateBounds(ref bounds, ref hasBounds, item.Area);
                }

                if (hasBounds && !IsRectVisible(bounds))
                    DrawEdgeMarker(drawList, bounds, UiColors.ForegroundFull.Fade(Blink));
            }
        }
    }

    private void DrawEdgeMarker(ImDrawListPtr drawList, in ImRect boundsOnCanvas, Color baseColor)
    {
        // The marker is a screen-space HUD element pinned to the window edge, so every size is in
        // scaled UI pixels (UiScaleFactor) — independent of canvas zoom (CanvasScale).
        var windowRect = ImRect.RectWithSize(WindowPos, WindowSize);
        var box = TransformRect(boundsOnCanvas);

        // Fade in over the first pixels past the edge so the marker doesn't pop the instant the box
        // leaves the view. The larger axis overshoot keeps it solid while crossing a corner.
        var gapX = MathF.Max(MathF.Max(windowRect.Min.X - box.Max.X, box.Min.X - windowRect.Max.X), 0f);
        var gapY = MathF.Max(MathF.Max(windowRect.Min.Y - box.Max.Y, box.Min.Y - windowRect.Max.Y), 0f);
        var fade = (MathF.Max(gapX, gapY) / (FadeInDistance * T3Ui.UiScaleFactor)).Clamp(0f, 1f);
        if (fade <= 0f)
            return;

        var color = baseColor.Fade(fade);
        var thickness = MarkerThickness * T3Ui.UiScaleFactor;
        var minLength = MarkerMinLength * T3Ui.UiScaleFactor;

        // The marker rides the rounded window border, inset so the centred stroke sits fully inside.
        var inner = windowRect;
        inner.Expand(-(EdgePadding * T3Ui.UiScaleFactor + thickness * 0.5f));
        var radius = MathF.Min(CornerRadius * T3Ui.UiScaleFactor,
                               MathF.Min(inner.Max.X - inner.Min.X, inner.Max.Y - inner.Min.Y) * 0.5f);
        var perimeter = BorderPerimeter(inner, radius);

        // Project the box's four corners onto the border as arc-length positions, then keep the
        // smallest arc that contains them all (drop the largest gap between consecutive positions).
        // This is one continuous span that slides along edges and wraps corners — no per-edge modes
        // whose boundaries could make the line jump.
        Span<float> positions = stackalloc float[4];
        positions[0] = BorderArcLength(inner, radius, new Vector2(box.Min.X, box.Min.Y));
        positions[1] = BorderArcLength(inner, radius, new Vector2(box.Max.X, box.Min.Y));
        positions[2] = BorderArcLength(inner, radius, new Vector2(box.Max.X, box.Max.Y));
        positions[3] = BorderArcLength(inner, radius, new Vector2(box.Min.X, box.Max.Y));
        positions.Sort();

        var gapStart = 0;
        var largestGap = positions[0] + perimeter - positions[3]; // the wrap-around gap
        for (var i = 1; i < 4; i++)
        {
            var gap = positions[i] - positions[i - 1];
            if (gap > largestGap)
            {
                largestGap = gap;
                gapStart = i;
            }
        }

        var spanStart = positions[gapStart];
        var spanLength = positions[(gapStart + 3) % 4] - spanStart;
        if (spanLength < 0)
            spanLength += perimeter;

        // Never shorter than the minimum; grow symmetrically along the border so it can't snap inward.
        if (spanLength < minLength)
        {
            spanStart -= (minLength - spanLength) * 0.5f;
            spanLength = minLength;
        }

        var segmentCount = (int)MathF.Max(2, MathF.Ceiling(spanLength / (4 * T3Ui.UiScaleFactor)));
        drawList.PathClear();
        for (var i = 0; i <= segmentCount; i++)
        {
            drawList.PathLineTo(BorderPointAt(inner, radius, spanStart + spanLength * ((float)i / segmentCount)));
        }

        drawList.PathStroke(color, ImDrawFlags.None, thickness);

        // Rounded end-caps.
        drawList.AddCircleFilled(BorderPointAt(inner, radius, spanStart), thickness * 0.5f, color);
        drawList.AddCircleFilled(BorderPointAt(inner, radius, spanStart + spanLength), thickness * 0.5f, color);
    }

    private static void AccumulateBounds(ref ImRect bounds, ref bool hasBounds, ImRect add)
    {
        if (!hasBounds)
        {
            bounds = add;
            hasBounds = true;
        }
        else
        {
            bounds.Add(add);
        }
    }

    private static float BorderPerimeter(in ImRect inner, float radius)
    {
        var straightX = inner.Max.X - inner.Min.X - 2 * radius;
        var straightY = inner.Max.Y - inner.Min.Y - 2 * radius;
        return 2 * straightX + 2 * straightY + 2 * MathF.PI * radius;
    }

    /// <summary>
    /// Arc-length position (clockwise from the top-left corner) of the point on the window's rounded
    /// border closest to <paramref name="p"/>. The four straight edges and four quarter-arcs are walked in order.
    /// </summary>
    private static float BorderArcLength(in ImRect inner, float radius, Vector2 p)
    {
        var halfPi = MathF.PI * 0.5f;
        var straightX = inner.Max.X - inner.Min.X - 2 * radius;
        var straightY = inner.Max.Y - inner.Min.Y - 2 * radius;
        var quarter = halfPi * radius;
        var coreMinX = inner.Min.X + radius;
        var coreMaxX = inner.Max.X - radius;
        var coreMinY = inner.Min.Y + radius;
        var coreMaxY = inner.Max.Y - radius;

        var atLeft = p.X < coreMinX;
        var atRight = p.X > coreMaxX;
        var atTop = p.Y < coreMinY;
        var atBottom = p.Y > coreMaxY;
        var midX = !atLeft && !atRight;

        if (midX && atTop) // top edge
            return (p.X - coreMinX).Clamp(0, straightX);

        if (atRight && atTop) // top-right arc
            return straightX + (MathF.Atan2(p.Y - coreMinY, p.X - coreMaxX).Clamp(-halfPi, 0) + halfPi) * radius;

        if (atRight && !atBottom) // right edge
            return straightX + quarter + (p.Y - coreMinY).Clamp(0, straightY);

        if (atRight && atBottom) // bottom-right arc
            return straightX + quarter + straightY + MathF.Atan2(p.Y - coreMaxY, p.X - coreMaxX).Clamp(0, halfPi) * radius;

        if (midX && atBottom) // bottom edge (right to left)
            return straightX + quarter + straightY + quarter + (coreMaxX - p.X).Clamp(0, straightX);

        if (atLeft && atBottom) // bottom-left arc
            return straightX + quarter + straightY + quarter + straightX
                   + (MathF.Atan2(p.Y - coreMaxY, p.X - coreMinX).Clamp(halfPi, MathF.PI) - halfPi) * radius;

        if (atLeft && !atTop) // left edge (bottom to top)
            return straightX + quarter + straightY + quarter + straightX + quarter + (coreMaxY - p.Y).Clamp(0, straightY);

        // top-left arc (atLeft && atTop)
        var angle = MathF.Atan2(p.Y - coreMinY, p.X - coreMinX);
        if (angle < 0)
            angle += 2 * MathF.PI;
        return straightX + quarter + straightY + quarter + straightX + quarter + straightY
               + (angle.Clamp(MathF.PI, MathF.PI + halfPi) - MathF.PI) * radius;
    }

    /// <summary>Inverse of <see cref="BorderArcLength"/>: the border point at arc-length <paramref name="s"/>.</summary>
    private static Vector2 BorderPointAt(in ImRect inner, float radius, float s)
    {
        var halfPi = MathF.PI * 0.5f;
        var straightX = inner.Max.X - inner.Min.X - 2 * radius;
        var straightY = inner.Max.Y - inner.Min.Y - 2 * radius;
        var quarter = halfPi * radius;
        var perimeter = 2 * straightX + 2 * straightY + 4 * quarter;

        s %= perimeter;
        if (s < 0)
            s += perimeter;

        if (s <= straightX) // top edge
            return new Vector2(inner.Min.X + radius + s, inner.Min.Y);
        s -= straightX;

        if (s <= quarter) // top-right arc
            return ArcPoint(new Vector2(inner.Max.X - radius, inner.Min.Y + radius), radius, -halfPi + s / radius);
        s -= quarter;

        if (s <= straightY) // right edge
            return new Vector2(inner.Max.X, inner.Min.Y + radius + s);
        s -= straightY;

        if (s <= quarter) // bottom-right arc
            return ArcPoint(new Vector2(inner.Max.X - radius, inner.Max.Y - radius), radius, s / radius);
        s -= quarter;

        if (s <= straightX) // bottom edge
            return new Vector2(inner.Max.X - radius - s, inner.Max.Y);
        s -= straightX;

        if (s <= quarter) // bottom-left arc
            return ArcPoint(new Vector2(inner.Min.X + radius, inner.Max.Y - radius), radius, halfPi + s / radius);
        s -= quarter;

        if (s <= straightY) // left edge
            return new Vector2(inner.Min.X, inner.Max.Y - radius - s);
        s -= straightY;

        // top-left arc
        return ArcPoint(new Vector2(inner.Min.X + radius, inner.Min.Y + radius), radius, MathF.PI + s / radius);
    }

    private static Vector2 ArcPoint(Vector2 center, float radius, float angle)
    {
        return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private const float EdgePadding = 3;
    private const float MarkerThickness = 2;
    private const float MarkerMinLength = 14;
    private const float CornerRadius = 8;
    private const float FadeInDistance = 50;
}
