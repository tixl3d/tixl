#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.TransformGizmos;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// This is a derived version of ConnectionSplit helper. That older version was tightly coupled with the
/// legacy graph window.
///
/// TODO:
/// - support hovering multiple connections
/// - indicate input / center / output region on the connection
/// 
/// </summary>
internal sealed class ConnectionHovering
{
    internal void PrepareNewFrame(GraphUiContext context)
    {
        _mousePosition = ImGui.GetMousePos();

        // A bend-point drag is driven first and on its own, not from the hover list. Dragging a handle
        // suppresses that cable's hover points, so routing the update through the hover path meant the
        // drag only advanced on the occasional frame that still had one - the handle appeared stuck.
        if (_bendPointDrag is { IsDragging: true })
        {
            UpdateBendPointDrag(context);
            return;
        }

        // Swap lists
        (_lastConnectionHovers, _connectionHoversForCurrentFrame) = (_connectionHoversForCurrentFrame, _lastConnectionHovers);
        _connectionHoversForCurrentFrame.Clear();

        if (!context.View.IsHovered)
            _lastConnectionHovers.Clear();

        // Bend points are always grabbable; Alt is only needed to ADD one to the cable. So the
        // bend-point path runs either when Alt is held or when the cursor is actually on a handle.
        if (_lastConnectionHovers.Count > 0 && UpdateBendPointIntent(context))
        {
            PrepareBendPointEditing(context);
            return;
        }

        if (_lastConnectionHovers.Count == 0)
        {
            StopHover();
            return;
        }

        var firstHover = _lastConnectionHovers[0];
        var time = ImGui.GetTime();
        if (_hoverStartTime < 0)
            _hoverStartTime = time;

        var hoverDuration = time - _hoverStartTime;
        var hoverIndicatorRadius = EaseFunctions.EaseOutElastic((float)hoverDuration) * 9;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddCircleFilled(firstHover.PositionOnScreen, hoverIndicatorRadius, firstHover.Color, 30);

        // For merged lines with matching types and line regions, we can offer multi drag...
        var bounds = ImRect.RectWithSize(firstHover.PositionOnScreen, Vector2.Zero);
        var firstType = firstHover.Connection.Type;
        var firstOutput = firstHover.Connection.SourceOutput;
        var typesMatch = true;
        var region = firstHover.Region;

        for (var index = 1; index < _lastConnectionHovers.Count; index++)
        {
            var h = _lastConnectionHovers[index];
            bounds.Add(h.PositionOnScreen);
            if (h.Connection.Type != firstType)
                typesMatch = false;

            if (region != h.Region)
            {
                region = LineRegions.Undefined;
                break;
            }

            if (firstOutput != h.Connection.SourceOutput)
                firstOutput = null;
        }

        var tooLarge = bounds.GetHeight() > 2 || bounds.GetWidth() > 2;
        bool disabled = UserSettings.Config.HoverMode == UserSettings.GraphHoverModes.Disabled;
        var isConsistentTypeAndOutput = !tooLarge && typesMatch && region != LineRegions.Undefined && firstOutput != null;
        if (isConsistentTypeAndOutput)
        {
            // We only draw first indicator, because hover points fall together closely...
            drawList.AddCircleFilled(firstHover.PositionOnScreen, hoverIndicatorRadius, firstHover.Color, 12);

            // Prepare disconnecting from input slot...
            if (region == LineRegions.End)
            {
                if (_lastConnectionHovers.Count == 1)
                {
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        ConnectionHoversWhenClicked.Clear();
                        ConnectionHoversWhenClicked.AddRange(_lastConnectionHovers);
                        context.StateMachine.SetState(GraphStates.HoldingConnectionEnd, context);
                    }

                    // Show indicator at end...
                    var inputPosInScreen = context.View.TransformPosition(firstHover.Connection.TargetPos);
                    drawList.AddCircle(inputPosInScreen, hoverIndicatorRadius, firstHover.Color, 24, 2);
                }
            }
            else if (region == LineRegions.Beginning)
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    ConnectionHoversWhenClicked.Clear();
                    ConnectionHoversWhenClicked.AddRange(_lastConnectionHovers);
                    context.StateMachine.SetState(GraphStates.HoldingConnectionBeginning, context);
                }

                var outputPosInScreen = context.View.TransformPosition(firstHover.Connection.SourcePos);
                drawList.AddCircle(outputPosInScreen, hoverIndicatorRadius, firstHover.Color, 24, 2);
            }

            if (!disabled)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
                ImGui.BeginTooltip();
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.TextUnformatted("Click to insert operator or\ndrag to disconnect...\nAlt-click to add a bend point");
                //ImGui.Spacing();
                FormInputs.AddVerticalSpace();
                ImGui.PopFont();
                ImGui.EndTooltip();
                ImGui.PopStyleVar();
            }
        }

        //else if (firstOutput != null)
        //{
        if (!disabled)
        {
            DrawTooltipForSingleOutput(firstHover);
        }
        // Inconsistent connection types...
        //}

        // TODO: Implement splitting
        // var buttonMin = _mousePosition - Vector2.One * radius / 2;
        // ImGui.SetCursorScreenPos(buttonMin);
        // if (ImGui.InvisibleButton("splitMe", Vector2.One * radius))
        // {
        //     var posOnScreen = context.Canvas.InverseTransformPositionFloat(bestMatchLastFrame.PositionOnScreen)
        //                       - new Vector2(SymbolUi.Child.DefaultOpSize.X * 0.25f,
        //                                     SymbolUi.Child.DefaultOpSize.Y * 0.5f);
        //
        //     // TODO: Implement correctly
        //     // ConnectionMaker.SplitConnectionWithSymbolBrowser(window, window.CompositionOp!.Symbol,
        //     //                                                  _bestMatchYetForCurrentFrame.Connection,
        //     //                                                  posOnScreen);
        // }
        // else
        // {
        //     StopHover();
        // }
        // _bestSnapSplitDistance = float.PositiveInfinity;
    }

    private static void DrawTooltipForSingleOutput(HoverPoint bestMatchLastFrame)
    {
        ImGui.BeginTooltip();
        {
            var connection = bestMatchLastFrame.Connection;
            var sourceOpInstance = connection.SourceItem.Instance;
            var outputSlot = connection.SourceOutput;

            var targetOp = connection.TargetItem.Instance;

            if (connection.SourceItem.SymbolUi != null
                && sourceOpInstance != null
                && outputSlot != null
                && targetOp != null
                && connection.TargetItem.SymbolUi != null)
            {
                //var width = 160f;
                ImGui.SetNextWindowSizeConstraints(new Vector2(200, 200 * 9 / 16f), new Vector2(200, 200 * 9 / 16f));

                var sourceOpUi = ToolTipContentDrawer.DrawForOutput(outputSlot, out var sourceOutputUi);
                if (sourceOutputUi != null)
                {
                    ImGui.PushFont(Fonts.FontSmall);
                    var connectionSource = sourceOpUi.Symbol.Name + "." + sourceOutputUi.OutputDefinition.Name;

                    ImGui.TextColored(UiColors.TextMuted, connectionSource);
                    var symbolChildInput = connection.TargetItem.SymbolUi.InputUis[connection.TargetInput.Id];

                    ImGui.SameLine();
                    var typeName = TypeNameRegistry.Entries.GetValueOrDefault(connection.Type, connection.Type.Name);
                    ImGui.TextUnformatted("<" + typeName + ">");
                    var inputIndex = connection.MultiInputIndex > 0 ? "[" + connection.MultiInputIndex + "]" : String.Empty;
                    var connectionTarget = "--> " + targetOp.Symbol.Name + "." + symbolChildInput.InputDefinition.Name + inputIndex;
                    ImGui.TextColored(UiColors.TextMuted, connectionTarget);
                    ImGui.PopFont();
                    
                    //var nodeSelection = context.Selector;
                    FrameStats.AddHoveredId(targetOp.SymbolChildId);
                    FrameStats.AddHoveredId(connection.SourceItem.Id);
                }
            }
        }
        ImGui.EndTooltip();
    }

    private void StopHover()
    {
        _hoverStartTime = -1;
        //HoveredInputConnection = null;
    }

    /// <summary>
    /// True while the canvas context menu must stay closed because the bend-point interaction is using
    /// this gesture. The menu opens on right-click <em>release</em> - a frame or more after the press
    /// that grabbed or removed a handle - so this is a short window rather than a live cursor test.
    /// </summary>
    internal bool IsContextMenuSuppressedForBendPoint => ImGui.GetTime() < _suppressContextMenuUntil;

    /// <summary>
    /// Holds the context menu closed briefly whenever a bend point is grabbed or removed. Without it,
    /// the right-click that removed a handle would also open the canvas menu over the cable.
    /// </summary>
    private void SuppressContextMenuBriefly()
    {
        _suppressContextMenuUntil = ImGui.GetTime() + ContextMenuSuppressionSec;
    }
    /// <summary>
    /// Decides whether the hovered cable should be handled by the bend-point interaction, and while
    /// <c>Alt</c> is held records where a new bend point would land so it can be shown to the user.
    /// </summary>
    /// <returns>True when the bend-point interaction should take over this frame.</returns>
    private bool UpdateBendPointIntent(GraphUiContext context)
    {
        _hasCandidateBendPoint = false;
        _candidateConnection = null;

        var mousePos = ImGui.GetMousePos();
        var isCursorOnHandle = false;
        MagGraphConnection? cableUnderCursor = null;

        foreach (var hover in _lastConnectionHovers)
        {
            if (ConnectionBendPoints.FindBendPointAt(context.View, hover.Connection, mousePos) >= 0)
            {
                isCursorOnHandle = true;
                break;
            }

            // Remember one hoverable cable, so holding Alt over it can offer a place to click.
            cableUnderCursor ??= hover.Connection;
        }

        if (isCursorOnHandle)
            return true;

        if (!ImGui.GetIO().KeyAlt || cableUnderCursor == null || cableUnderCursor.IsSnapped)
            return false;

        _hasCandidateBendPoint = true;
        _candidateConnection = cableUnderCursor;
        return true;
    }

    /// <summary>
    /// Handles editing the bend points that let a connection line be routed around the nodes it
    /// crosses. Dragging an existing handle needs no modifier, since the handles are visible and are
    /// the intended target; <c>Alt</c> is only needed to drop a new one onto the cable.
    /// </summary>
    private void PrepareBendPointEditing(GraphUiContext context)
    {
        // An active drag is driven from PrepareNewFrame, before the hover list is considered, so there
        // is nothing to do here until it ends.
        if (_bendPointDrag is { IsDragging: true })
        {
            _lastConnectionHovers.Clear();
            return;
        }

        var connection = _lastConnectionHovers[0].Connection;
        var bendPointIndex = ConnectionBendPoints.FindBendPointAt(context.View, connection, ImGui.GetMousePos());

        // A snapped cable is collapsed to a point, so it has no length to bend and a bend point left on
        // one by an earlier edit must not be draggable - moving it would only collapse the cable.
        if (connection.IsSnapped)
        {
            _lastConnectionHovers.Clear();
            return;
        }

        if (bendPointIndex >= 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            SuppressContextMenuBriefly();
            StartBendPointDrag(context, connection, bendPointIndex);
            _lastConnectionHovers.Clear();
            return;
        }

        if (bendPointIndex >= 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            // The menu opens on right-click RELEASE, a frame or more after this press, so the
            // suppression has to outlive the press that removed the handle.
            SuppressContextMenuBriefly();
            RemoveBendPointAtCursor(context, connection, bendPointIndex);
            _lastConnectionHovers.Clear();
            return;
        }

        // Alt remains the way to start a new bend point, so the cable keeps its normal
        // click-to-insert-operator behaviour.
        if (!ImGui.GetIO().KeyAlt)
            return;

        // Snapped routes are collapsed to a point between adjacent operators, so they have no visible
        // length to bend and a bend point there would only collapse the line.
        if (connection.IsSnapped)
            return;

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            StartBendPointDrag(context, connection, -1);
            _lastConnectionHovers.Clear();
        }
    }

    /// <summary>
    /// The bend point currently being dragged on a connection, or -1. Used to render the grabbed handle.
    /// </summary>
    internal int DraggedBendPointIndex(MagGraphConnection connection)
    {
        return _bendPointDrag is { IsDragging: true } drag && drag.Connection == connection ? drag.Index : -1;
    }

    /// <param name="existingBendPointIndex">
    /// Index of the handle being dragged, or -1 to insert a new bend point where the cursor sits on the cable.
    /// </param>
    private void StartBendPointDrag(GraphUiContext context, MagGraphConnection connection, int existingBendPointIndex)
    {
        var waypoints = connection.GetWaypoints();
        var screenPos = ImGui.GetMousePos();

        var drag = new BendPointDrag
                   {
                       Connection = connection,
                       OriginalWaypoints = waypoints == null ? null : [..waypoints],
                       Waypoints = waypoints == null ? [] : [..waypoints],
                   };

        if (existingBendPointIndex >= 0)
        {
            drag.Index = existingBendPointIndex;
        }
        else
        {
            var points = CanvasPoints(connection, context);
            var anchors = BuildAnchors(context, connection, points, out var segmentIndices);

            var segment = FindClosestSegment(anchors, screenPos);
            drag.Index = segment < 0 ? drag.Waypoints.Count : segmentIndices[segment];

            drag.Waypoints.Insert(drag.Index, context.View.InverseTransformPositionFloat(screenPos));
            drag.WasInserted = true;
        }

        drag.IsDragging = true;
        _bendPointDrag = drag;

        MoveBendPointToCursor(context);
    }

    private void UpdateBendPointDrag(GraphUiContext context)
    {
        if (_bendPointDrag is not { IsDragging: true } drag)
            return;

        var isCancelled = ImGui.IsKeyDown(ImGuiKey.Escape);

        if (!isCancelled)
        {
            MoveBendPointToCursor(context);
        }

        // The release event, not the button state: this now runs every frame for the whole drag, so the
        // event cannot be missed. Keying off the state and bailing out while it read "up" is what made
        // the handle only advance on stray frames.
        var isReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

        if (!isReleased && !isCancelled)
            return;

        drag.IsDragging = false;
        _bendPointDrag = null;

        if (isCancelled)
        {
            drag.Connection.SourceItem.ChildUi?.SetConnectionWaypoints(drag.Connection.WaypointTarget, drag.OriginalWaypoints ?? []);
            return;
        }

        if ((drag.WasMoved || drag.WasInserted) && drag.Connection.SourceItem.ChildUi != null)
        {
            UndoRedoStack.AddAndExecute(new ChangeConnectionWaypointsCommand(context.CompositionInstance.Symbol.Id,
                                                                             drag.Connection.SourceItem.ChildUi.Id,
                                                                             drag.Connection.WaypointTarget,
                                                                             drag.OriginalWaypoints,
                                                                             drag.Waypoints));
        }
    }

    private void MoveBendPointToCursor(GraphUiContext context)
    {
        if (_bendPointDrag is not { } drag || drag.Index < 0 || drag.Index >= drag.Waypoints.Count)
            return;

        var newPosition = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());
        if (Vector2.DistanceSquared(newPosition, drag.Waypoints[drag.Index]) < 0.0001f)
            return;

        drag.Waypoints[drag.Index] = newPosition;
        drag.Connection.SourceItem.ChildUi?.SetConnectionWaypointsOwned(drag.Connection.WaypointTarget, drag.Waypoints);
        drag.WasMoved = true;
    }

    private static void RemoveBendPointAtCursor(GraphUiContext context, MagGraphConnection connection, int bendPointIndex)
    {
        var waypoints = connection.GetWaypoints();
        if (bendPointIndex < 0 || waypoints == null || connection.SourceItem.ChildUi == null)
            return;

        var remaining = new List<Vector2>(waypoints);
        remaining.RemoveAt(bendPointIndex);

        UndoRedoStack.AddAndExecute(new ChangeConnectionWaypointsCommand(context.CompositionInstance.Symbol.Id,
                                                                         connection.SourceItem.ChildUi.Id,
                                                                         connection.WaypointTarget,
                                                                         waypoints,
                                                                         remaining));
    }

    private static List<Vector2> CanvasPoints(MagGraphConnection connection, GraphUiContext context)
    {
        var points = new List<Vector2>(4);

        if (connection.SourceItem.IsCollapsedAway)
        {
            if (!context.Layout.Sections.TryGetValue(connection.SourceItem.ChildUi!.HiddenInCollapsedSectionId, out var sourceSection))
                return points;

            points.Add(sourceSection.PosOnCanvas + new Vector2(sourceSection.Size.X - 5, MagGraphItem.LineHeight / 2));
        }
        else
        {
            // SourcePos, not DampedSourcePos: the layout writes SourcePos every frame, while the damped
            // copy is the drawn position a bend point must line up with.
            points.Add(connection.SourcePos);
        }

        if (connection.TargetItem.IsCollapsedAway)
        {
            if (context.Layout.Sections.TryGetValue(connection.TargetItem.ChildUi!.HiddenInCollapsedSectionId, out var targetSection))
            {
                points.Add(targetSection.PosOnCanvas + new Vector2(2, MagGraphItem.LineHeight / 2));
            }

            return points;
        }

        connection.AppendWaypoints(points, out _);
        points.Add(connection.TargetPos);
        return points;
    }

    /// <summary>
    /// Expands the connection's canvas points into the screen-space anchors a bend point can be
    /// attached to, plus where each anchor's segment begins in the saved waypoint list.
    /// </summary>
    private List<Vector2> BuildAnchors(GraphUiContext context, MagGraphConnection connection, List<Vector2> canvasPoints, out List<int> segmentIndices)
    {
        segmentIndices = new List<int>(canvasPoints.Count);

        var waypointCount = connection.GetWaypoints()?.Count ?? 0;
        var last = canvasPoints.Count - 1;

        for (var index = 0; index < canvasPoints.Count; index++)
        {
            segmentIndices.Add(index == last ? waypointCount : Math.Min(index, waypointCount));
        }

        var anchors = new List<Vector2>(canvasPoints.Count);
        foreach (var point in canvasPoints)
        {
            anchors.Add(context.View.TransformPosition(point));
        }

        return anchors;
    }

    private static int FindClosestSegment(List<Vector2> anchors, Vector2 position)
    {
        const float maximumDistance = 12;

        var bestSegment = -1;
        var bestDistance = maximumDistance;

        for (var index = 0; index < anchors.Count - 1; index++)
        {
            var distance = DistanceToSegment(anchors[index], anchors[index + 1], position);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestSegment = index;
        }

        return bestSegment;
    }

    private static float DistanceToSegment(Vector2 start, Vector2 end, Vector2 position)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared < 0.0001f)
            return Vector2.Distance(start, position);

        var t = Math.Clamp(Vector2.Dot(position - start, segment) / lengthSquared, 0f, 1f);
        return Vector2.Distance(start + segment * t, position);
    }

    private sealed class BendPointDrag
    {
        internal MagGraphConnection Connection = null!;
        internal List<Vector2> Waypoints = null!;
        internal List<Vector2>? OriginalWaypoints;
        internal int Index;
        internal bool IsDragging;
        internal bool WasMoved;

        /// <summary>A point was added, which has to be committed even if the cursor never moved.</summary>
        internal bool WasInserted;
    }

    private BendPointDrag? _bendPointDrag;

    private bool _hasCandidateBendPoint;

    /// <summary>How long the canvas context menu stays closed after a bend-point gesture.</summary>
    private const double ContextMenuSuppressionSec = 0.25;

    private double _suppressContextMenuUntil;

    /// <summary>
    /// True while a bend point is being dragged. Other canvas interactions - the selection fence in
    /// particular - have to stand down, because a drag keeps the state machine in its idle state.
    /// </summary>
    internal bool IsDraggingBendPoint => _bendPointDrag is { IsDragging: true };

    /// <summary>The cable the candidate bend point belongs to, so only that cable shows the marker.</summary>
    private MagGraphConnection? _candidateConnection;

    /// <summary>
    /// True when a new bend point would currently be dropped onto <paramref name="connection"/>.
    /// </summary>
    internal bool IsShowingCandidateFor(MagGraphConnection connection)
    {
        return _hasCandidateBendPoint && _candidateConnection == connection;
    }

    /// <summary>
    /// Where to draw the insertion marker on a cable that still follows its automatic route: the hover
    /// point nearest the cursor, which is the spot a click would insert at.
    /// </summary>
    internal Vector2 ClosestCandidatePositionOnScreen
    {
        get
        {
            var mousePos = ImGui.GetMousePos();
            var best = Vector2.Zero;
            var bestDistanceSquared = float.MaxValue;

            foreach (var hover in _lastConnectionHovers)
            {
                if (hover.Connection != _candidateConnection)
                    continue;

                var distanceSquared = Vector2.DistanceSquared(mousePos, hover.PositionOnScreen);
                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                best = hover.PositionOnScreen;
            }

            return best;
        }
    }

    internal enum LineRegions
    {
        Undefined,
        Beginning,
        Center,
        End,
    }

    internal static bool IsHovered(MagGraphConnection connection)
    {
        foreach (var h in _lastConnectionHovers)
        {
            if (h.Connection == connection)
                return true;
        }

        return false;
    }

    public static void RegisterHoverPoint(MagGraphConnection mcConnection, Color color, Vector2 positionOnScreen, float normalizedPosition,
                                          Vector2 sourcePosOnScreen)
    {
        var overlapWithOutputThreshold = 10;
        if (Vector2.Distance(positionOnScreen, sourcePosOnScreen) < overlapWithOutputThreshold)
            return;

        const float threshold = 0.5f;
        var region = normalizedPosition switch
                         {
                             < threshold     => LineRegions.Beginning,
                             > 1 - threshold => LineRegions.End,
                             _               => LineRegions.Center
                         };

        _connectionHoversForCurrentFrame.Add(new HoverPoint(positionOnScreen, normalizedPosition, mcConnection, color, region));
    }

    //internal MagGraphConnection? HoveredInputConnection;

    internal readonly List<HoverPoint> ConnectionHoversWhenClicked = [];
    private static List<HoverPoint> _lastConnectionHovers = [];
    private static List<HoverPoint> _connectionHoversForCurrentFrame = []; // deferred, because hovered is computed during draw.

    //private static float _bestSnapSplitDistance = float.PositiveInfinity;
    private const int SnapDistance = 50;
    private static Vector2 _mousePosition;
    private static double _hoverStartTime = -1;

    public sealed record HoverPoint(
        Vector2 PositionOnScreen,
        float NormalizedDistanceOnLine,
        MagGraphConnection Connection,
        Color Color,
        LineRegions Region);
}

internal static class ToolTipContentDrawer
{
    internal static SymbolUi DrawForOutput(ISlot outputSlot, out IOutputUi? sourceOutputUi)
    {
        var width = (int)(170 * T3Ui.UiScaleFactor);
        var drawList = ImGui.GetWindowDrawList();
        var sourceOpInstance = outputSlot.Parent;
        var sourceOpUi = sourceOpInstance.GetSymbolUi();

        if (!sourceOpUi.OutputUis.TryGetValue(outputSlot.Id, out sourceOutputUi))
            return sourceOpUi;

        // Check if we should skip thumbnail rendering for LastValue mode with Texture2D
        bool skipTexture2D = UserSettings.Config.HoverMode == UserSettings.GraphHoverModes.LastValue &&
                             outputSlot.ValueType == typeof(Texture2D);

        if (UserSettings.Config.HoverMode == UserSettings.GraphHoverModes.Disabled)
            return sourceOpUi;

        if (skipTexture2D)
            return sourceOpUi;

        ImGui.BeginChild("thumbnail", new Vector2(width, width * 9 / 16f));
        {
            TransformGizmoHandling.SetDrawList(drawList);
            _imageCanvasForTooltips.Update();
            _imageCanvasForTooltips.SetAsCurrent();
            _evaluationContext.Reset();

            _evaluationContext.RequestedResolution = RenderProcess.OutputWindow != null 
                ? RenderProcess.OutputWindow.RequestedResolution 
                : new Int2(1280 / 2, 1720 / 2);
            
            sourceOutputUi.DrawValue(outputSlot,
                                     _evaluationContext,
                                     "connectionLineThumbnail",
                                     recompute: UserSettings.Config.HoverMode == UserSettings.GraphHoverModes.Live);

            ImageOutputCanvas.Deactivate();
            TransformGizmoHandling.RestoreDrawList();
        }
        ImGui.EndChild();
        return sourceOpUi;
    }

    private static readonly ImageOutputCanvas _imageCanvasForTooltips = new() { DisableDamping = true };
    private static readonly EvaluationContext _evaluationContext = new();
}