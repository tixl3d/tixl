#nullable enable
using ImGuiNET;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Moves a focus between the entries of an immediate-mode list or column grid with directional input, without
/// ImGui's own navigation. Entries call <see cref="Register"/> right after drawing; a move ranks the rects of the
/// previous frame, so it doesn't depend on draw order. Entries are identified by their draw order, which has to stay
/// stable while the layout doesn't change.
/// </summary>
/// <remarks>
/// Moves never change the axis: up and down stay in the column, left and right go to the next column and pick the
/// entry closest in height there. Only past the end of a column, up and down continue in the neighbouring column.
/// </remarks>
internal sealed class FocusNavigator
{
    internal bool HasFocus => _focusedIndex >= 0;

    /** Removes the focus until the next move, e.g. when the content changes or the mouse took over. */
    internal void Reset()
    {
        _focusedIndex = NoFocus;
        _rects.Clear();
        _lastFrameRects.Clear();
    }

    /** Call before drawing the entries. */
    internal void BeginFrame()
    {
        (_lastFrameRects, _rects) = (_rects, _lastFrameRects);
        _rects.Clear();

        if (_focusedIndex >= _lastFrameRects.Count && _lastFrameRects.Count > 0)
            _focusedIndex = _lastFrameRects.Count - 1;
    }

    /** Focuses the first entry drawn, which is the top left one in column layouts. */
    internal void FocusFirst()
    {
        _focusedIndex = 0;
        _scrollToFocus = true;
    }

    /** Focuses the entry drawn at <paramref name="index"/> in this frame, e.g. to return to where a drill-down started. */
    internal void Focus(int index)
    {
        _focusedIndex = index;
        _scrollToFocus = true;
    }

    /// <summary>
    /// Moves the focus to the nearest entry in <paramref name="direction"/>. Without a focus the first entry gets
    /// it, so the first key press only shows where the focus is.
    /// </summary>
    internal void Move(SelectionNavigation.Directions direction)
    {
        if (_lastFrameRects.Count == 0)
            return;

        if (!HasFocus)
        {
            FocusFirst();
            return;
        }

        var bestIndex = FindTarget(direction, wrapToNextColumn: false);
        if (bestIndex == NoFocus && direction is SelectionNavigation.Directions.Up or SelectionNavigation.Directions.Down)
            bestIndex = FindTarget(direction, wrapToNextColumn: true);

        if (bestIndex == NoFocus)
            return;

        _focusedIndex = bestIndex;
        _scrollToFocus = true;
    }

    /// <summary>
    /// Records the last drawn ImGui item as the next entry. Draws the focus frame and scrolls it into view if it has
    /// the focus.
    /// </summary>
    /// <returns>The entry's index, for <see cref="Focus"/></returns>
    internal int Register(out bool isFocused)
    {
        var index = _rects.Count;
        var rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        _rects.Add(rect);

        isFocused = index == _focusedIndex;
        if (!isFocused)
            return index;

        DrawFrame(rect);

        if (_scrollToFocus)
        {
            ScrollIntoView(rect);
            _scrollToFocus = false;
        }

        return index;
    }

    private int FindTarget(SelectionNavigation.Directions direction, bool wrapToNextColumn)
    {
        var current = _lastFrameRects[_focusedIndex];
        var bestIndex = NoFocus;
        var bestDistance = float.PositiveInfinity;
        var bestOffset = float.PositiveInfinity;
        for (var index = 0; index < _lastFrameRects.Count; index++)
        {
            if (index == _focusedIndex)
                continue;

            var candidate = _lastFrameRects[index];
            var isCandidate = wrapToNextColumn
                                  ? TryGetWrapDistance(current, candidate, direction, out var distance, out var sidewaysOffset)
                                  : TryGetDistance(current, candidate, direction, out distance, out sidewaysOffset);
            if (!isCandidate)
                continue;

            // The nearest row or column wins; entries in it are ranked by how far they are off to the side
            var isNearer = distance < bestDistance - SameLineTolerancePx;
            var isSameLineButCloser = MathF.Abs(distance - bestDistance) <= SameLineTolerancePx && sidewaysOffset < bestOffset;
            if (!isNearer && !isSameLineButCloser)
                continue;

            bestDistance = distance;
            bestOffset = sidewaysOffset;
            bestIndex = index;
        }

        return bestIndex;
    }

    /// <summary>
    /// Continues a vertical move past the end of a column in reading order: down goes to the top of the next column,
    /// up to the bottom of the previous one.
    /// </summary>
    private static bool TryGetWrapDistance(ImRect from, ImRect to, SelectionNavigation.Directions direction,
                                           out float distance, out float sidewaysOffset)
    {
        if (direction == SelectionNavigation.Directions.Down)
        {
            distance = to.Min.X - from.Min.X;
            sidewaysOffset = to.Min.Y;
            return to.Min.X >= from.Max.X - SameLineTolerancePx;
        }

        distance = from.Min.X - to.Min.X;
        sidewaysOffset = -to.Max.Y;
        return to.Max.X <= from.Min.X + SameLineTolerancePx;
    }

    /// <summary>
    /// Candidates must lie entirely beyond the current entry in the direction. Vertical moves also need the candidate
    /// to overlap horizontally, which keeps them in the column.
    /// </summary>
    /// <param name="distance">Between the entries' left or top edges, where the entries of a column or row line up</param>
    private static bool TryGetDistance(ImRect from, ImRect to, SelectionNavigation.Directions direction,
                                       out float distance, out float sidewaysOffset)
    {
        var fromCenter = from.GetCenter();
        var toCenter = to.GetCenter();
        var overlapsHorizontally = to.Min.X < from.Max.X && to.Max.X > from.Min.X;

        switch (direction)
        {
            case SelectionNavigation.Directions.Up:
                distance = from.Min.Y - to.Min.Y;
                sidewaysOffset = MathF.Abs(toCenter.X - fromCenter.X);
                return overlapsHorizontally && to.Max.Y <= from.Min.Y + SameLineTolerancePx;

            case SelectionNavigation.Directions.Down:
                distance = to.Min.Y - from.Min.Y;
                sidewaysOffset = MathF.Abs(toCenter.X - fromCenter.X);
                return overlapsHorizontally && to.Min.Y >= from.Max.Y - SameLineTolerancePx;

            case SelectionNavigation.Directions.Left:
                distance = from.Min.X - to.Min.X;
                sidewaysOffset = MathF.Abs(toCenter.Y - fromCenter.Y);
                return to.Max.X <= from.Min.X + SameLineTolerancePx;

            case SelectionNavigation.Directions.Right:
                distance = to.Min.X - from.Min.X;
                sidewaysOffset = MathF.Abs(toCenter.Y - fromCenter.Y);
                return to.Min.X >= from.Max.X - SameLineTolerancePx;

            default:
                distance = float.PositiveInfinity;
                sidewaysOffset = float.PositiveInfinity;
                return false;
        }
    }

    private static void DrawFrame(ImRect rect)
    {
        var padding = new Vector2(3, 1) * T3Ui.UiScaleFactor;
        var min = Floor(rect.Min - padding);
        var max = Floor(rect.Max + padding);
        var thickness = MathF.Max(1, MathF.Round(FrameThickness * T3Ui.UiScaleFactor));
        ImGui.GetWindowDrawList().AddRect(min, max, UiColors.ForegroundFull.Fade(FrameOpacity), 3 * T3Ui.UiScaleFactor,
                                          ImDrawFlags.None, thickness);
    }

    private static Vector2 Floor(Vector2 v)
    {
        return new Vector2(MathF.Floor(v.X), MathF.Floor(v.Y));
    }

    /** Scrolls the current window vertically only if the entry is cut off, so short moves don't make the content jump. */
    private static void ScrollIntoView(ImRect rect)
    {
        var windowMinY = ImGui.GetWindowPos().Y;
        var windowMaxY = windowMinY + ImGui.GetWindowSize().Y;
        if (rect.Min.Y >= windowMinY && rect.Max.Y <= windowMaxY)
            return;

        ImGui.SetScrollHereY(0.5f);
    }

    private const int NoFocus = -1;

    /** Entries of one column start at the same indent, but text rects can differ by a pixel. */
    private const float SameLineTolerancePx = 2;
    private const float FrameThickness = 1;
    private const float FrameOpacity = 0.7f;

    private int _focusedIndex = NoFocus;
    private bool _scrollToFocus;
    private List<ImRect> _rects = [];
    private List<ImRect> _lastFrameRects = [];
}
