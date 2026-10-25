#nullable enable
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.OutputSetup;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Moves the selection to the neighbouring item in a direction (cursor-key navigation) and scrolls the view to
/// keep it visible. Neighbours are ranked by the gap between item rects along the direction, with a penalty for
/// sideways offset, so snapped stacks are walked one item at a time. Items connected to the current one win ties
/// against unconnected items at a similar distance.
/// Moves are recorded in a trail, so pressing the opposite key always walks back the way the cursor came, even
/// where the geometric ranking would pick a different item.
/// Keyboard navigation counts as active until the selection changes by other means (click, fence, ...); only
/// then are the jump targets marked and the trail kept.
/// </summary>
internal static class SelectionNavigation
{
    internal enum Directions
    {
        Up,
        Right,
        Down,
        Left,
    }

    private readonly record struct TrailStep(Guid FromId, Guid ToId, Directions Direction);

    internal static void MoveSelection(GraphUiContext context, Directions direction)
    {
        if (!IsKeyboardNavigationActive(context))
            _trail.Clear();

        var current = GetCursorItem(context);
        if (current == null)
        {
            var closestItem = FindItemClosestToViewCenter(context);
            if (closestItem != null)
                SelectAndReveal(context, closestItem, direction);

            return;
        }

        CollectConnectedItemIds(context.Layout, current);
        var target = FindTarget(context.Layout, current, direction, out var isStepBack);
        if (target == null)
            return;

        if (isStepBack)
        {
            _trail.RemoveAt(_trail.Count - 1);
        }
        else
        {
            if (_trail.Count > 0 && _trail[^1].ToId != current.Id)
                _trail.Clear();

            if (_trail.Count >= MaxTrailLength)
                _trail.RemoveAt(0);

            _trail.Add(new TrailStep(current.Id, target.Id, direction));
        }

        SelectAndReveal(context, target, direction);
    }

    /// <summary>
    /// Marks the side of each item an arrow key would select next, facing the current item. After each move the
    /// marks grow in staggered: the pressed direction first, then the sideways ones, then the way back.
    /// </summary>
    internal static void DrawTargetIndicators(GraphUiContext context, ImDrawListPtr drawList)
    {
        if (!context.View.IsFocused
            || context.StateMachine.CurrentState != GraphStates.Default
            || !IsKeyboardNavigationActive(context))
            return;

        var current = GetCursorItem(context);
        if (current == null)
            return;

        CollectConnectedItemIds(context.Layout, current);
        var timeSinceMove = (float)(ImGui.GetTime() - _lastMoveTime);
        var inset = IndicatorInsetPx * T3Ui.UiScaleFactor;
        for (var direction = Directions.Up; direction <= Directions.Left; direction++)
        {
            var stagger = direction == _lastMoveDirection
                              ? 0
                              : direction == GetOpposite(_lastMoveDirection)
                                  ? 2
                                  : 1;
            var revealStart = stagger * IndicatorStaggerSec;
            var revealProgress = Math.Clamp((timeSinceMove - revealStart) / IndicatorRevealSec, 0, 1);
            if (revealProgress <= 0)
                continue;

            var reveal = MathUtils.SmootherStep(0, 1, revealProgress);

            var target = FindTarget(context.Layout, current, direction, out _);
            if (target == null)
                continue;

            var area = context.View.TransformRect(target.Area);
            var screenScale = area.GetHeight() / target.Area.GetHeight();
            var center = area.GetCenter();
            var halfWidth = area.GetWidth() * IndicatorLengthFraction / 2 * reveal;
            var minHeightOnCanvas = MagGraphItem.GridSize.Y * IndicatorMinHeightFraction;
            var heightOnCanvas = MathF.Min(MathF.Max(target.Area.GetHeight() * IndicatorLengthFraction, minHeightOnCanvas),
                                           target.Area.GetHeight());
            var halfHeight = heightOnCanvas * screenScale / 2 * reveal;

            // The marked side is the one facing back toward the current item.
            var (from, to) = direction switch
                                 {
                                     Directions.Up    => (new Vector2(center.X - halfWidth, area.Max.Y - inset), new Vector2(center.X + halfWidth, area.Max.Y - inset)),
                                     Directions.Down  => (new Vector2(center.X - halfWidth, area.Min.Y + inset), new Vector2(center.X + halfWidth, area.Min.Y + inset)),
                                     Directions.Left  => (new Vector2(area.Max.X - inset, center.Y - halfHeight), new Vector2(area.Max.X - inset, center.Y + halfHeight)),
                                     _                => (new Vector2(area.Min.X + inset, center.Y - halfHeight), new Vector2(area.Min.X + inset, center.Y + halfHeight)),
                                 };

            SetupStrokes.DrawCrispLine(drawList, from, to, UiColors.ForegroundFull.Fade(IndicatorOpacity * revealProgress), IndicatorWidth);
        }
    }

    /** The last selected item is the cursor; with multiple selected, navigation continues from it. */
    private static MagGraphItem? GetCursorItem(GraphUiContext context)
    {
        var selection = context.Selector.Selection;
        for (var index = selection.Count - 1; index >= 0; index--)
        {
            if (context.Layout.Items.TryGetValue(selection[index].Id, out var item))
                return item;
        }

        return null;
    }

    private static void SelectAndReveal(GraphUiContext context, MagGraphItem item, Directions direction)
    {
        item.Select(context.Selector);
        context.View.ScrollToMakeAreaVisible(item.Area, EdgeMarginPx * T3Ui.UiScaleFactor);

        _selectionChangeCounterAfterMove = context.Selector.ChangeCounter;
        _lastMoveDirection = direction;
        _lastMoveTime = ImGui.GetTime();
    }

    private static bool IsKeyboardNavigationActive(GraphUiContext context)
    {
        return context.Selector.ChangeCounter == _selectionChangeCounterAfterMove;
    }

    /// <remarks>Requires <see cref="CollectConnectedItemIds"/> for <paramref name="current"/>.</remarks>
    private static MagGraphItem? FindTarget(MagGraphLayout layout, MagGraphItem current, Directions direction, out bool isStepBack)
    {
        isStepBack = false;
        if (_trail.Count > 0)
        {
            var lastStep = _trail[^1];
            if (lastStep.ToId == current.Id
                && direction == GetOpposite(lastStep.Direction)
                && layout.Items.TryGetValue(lastStep.FromId, out var previousItem)
                && IsNavigable(previousItem))
            {
                isStepBack = true;
                return previousItem;
            }
        }

        return FindNeighbour(layout, current, direction);
    }

    private static MagGraphItem? FindNeighbour(MagGraphLayout layout, MagGraphItem current, Directions direction)
    {
        MagGraphItem? bestItem = null;
        var bestCost = float.PositiveInfinity;
        foreach (var item in layout.Items.Values)
        {
            if (item == current || !IsNavigable(item))
                continue;

            if (!TryGetCost(current.Area, item.Area, direction, out var cost))
                continue;

            if (_connectedItemIds.Contains(item.Id))
                cost *= ConnectedCostFactor;

            if (cost >= bestCost)
                continue;

            bestCost = cost;
            bestItem = item;
        }

        return bestItem;
    }

    private static void CollectConnectedItemIds(MagGraphLayout layout, MagGraphItem current)
    {
        _connectedItemIds.Clear();
        foreach (var connection in layout.MagConnections)
        {
            if (connection.SourceItem == current)
                _connectedItemIds.Add(connection.TargetItem.Id);
            else if (connection.TargetItem == current)
                _connectedItemIds.Add(connection.SourceItem.Id);
        }
    }

    private static MagGraphItem? FindItemClosestToViewCenter(GraphUiContext context)
    {
        var viewCenter = context.View.GetVisibleCanvasArea().GetCenter();

        MagGraphItem? bestItem = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var item in context.Layout.Items.Values)
        {
            if (!IsNavigable(item))
                continue;

            var distance = Vector2.DistanceSquared(item.Area.GetCenter(), viewCenter);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestItem = item;
        }

        return bestItem;
    }

    private static bool IsNavigable(MagGraphItem item)
    {
        return item.Variant != MagGraphItem.Variants.Placeholder && !item.IsCollapsedAway;
    }

    private static Directions GetOpposite(Directions direction)
    {
        return (Directions)(((int)direction + 2) % 4);
    }

    /// <returns>False if the candidate does not lie in the requested direction</returns>
    private static bool TryGetCost(ImRect from, ImRect to, Directions direction, out float cost)
    {
        var fromCenter = from.GetCenter();
        var toCenter = to.GetCenter();

        float centerAdvance, gap, sidewaysGap, sidewaysCenterOffset;
        switch (direction)
        {
            case Directions.Up:
                centerAdvance = fromCenter.Y - toCenter.Y;
                gap = from.Min.Y - to.Max.Y;
                sidewaysGap = GetIntervalGap(from.Min.X, from.Max.X, to.Min.X, to.Max.X);
                sidewaysCenterOffset = MathF.Abs(toCenter.X - fromCenter.X);
                break;
            case Directions.Down:
                centerAdvance = toCenter.Y - fromCenter.Y;
                gap = to.Min.Y - from.Max.Y;
                sidewaysGap = GetIntervalGap(from.Min.X, from.Max.X, to.Min.X, to.Max.X);
                sidewaysCenterOffset = MathF.Abs(toCenter.X - fromCenter.X);
                break;
            case Directions.Left:
                centerAdvance = fromCenter.X - toCenter.X;
                gap = from.Min.X - to.Max.X;
                sidewaysGap = GetIntervalGap(from.Min.Y, from.Max.Y, to.Min.Y, to.Max.Y);
                sidewaysCenterOffset = MathF.Abs(toCenter.Y - fromCenter.Y);
                break;
            case Directions.Right:
                centerAdvance = toCenter.X - fromCenter.X;
                gap = to.Min.X - from.Max.X;
                sidewaysGap = GetIntervalGap(from.Min.Y, from.Max.Y, to.Min.Y, to.Max.Y);
                sidewaysCenterOffset = MathF.Abs(toCenter.Y - fromCenter.Y);
                break;
            default:
                cost = float.PositiveInfinity;
                return false;
        }

        if (centerAdvance <= MinCenterAdvance)
        {
            cost = float.PositiveInfinity;
            return false;
        }

        cost = MathF.Max(gap, 0) + sidewaysGap * SidewaysGapPenalty + sidewaysCenterOffset * SidewaysCenterPenalty;
        return true;
    }

    /** Distance between two 1D intervals, 0 if they overlap */
    private static float GetIntervalGap(float minA, float maxA, float minB, float maxB)
    {
        return MathF.Max(0, MathF.Max(minB - maxA, minA - maxB));
    }

    /** Screen distance kept between the selected item and the view edge when scrolling to it */
    private const float EdgeMarginPx = 80;

    /** Items whose centers barely move along the direction count as beside, not beyond the current one */
    private const float MinCenterAdvance = 1;

    private const float SidewaysGapPenalty = 3;
    private const float SidewaysCenterPenalty = 0.1f;
    private const float ConnectedCostFactor = 0.6f;

    private const int MaxTrailLength = 64;

    private const float IndicatorOpacity = 0.35f;
    private const float IndicatorWidth = 2;
    private const float IndicatorInsetPx = 3;
    private const float IndicatorLengthFraction = 0.4f;

    /** Vertical marks stay readable on single-line items */
    private const float IndicatorMinHeightFraction = 0.8f;

    /** Each mark grows in (eased) and fades in (linear) over this time; with the stagger, all marks are complete after 0.15 s */
    private const float IndicatorRevealSec = 0.06f;
    private const float IndicatorStaggerSec = 0.045f;

    private static readonly HashSet<Guid> _connectedItemIds = [];
    private static readonly List<TrailStep> _trail = new(MaxTrailLength);
    private static int _selectionChangeCounterAfterMove = -1;
    private static Directions _lastMoveDirection;
    private static double _lastMoveTime;
}
