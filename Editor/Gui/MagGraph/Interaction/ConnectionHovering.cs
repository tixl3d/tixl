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
using T3.Editor.UiModel;
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

        // Swap lists
        (_lastConnectionHovers, _connectionHoversForCurrentFrame) = (_connectionHoversForCurrentFrame, _lastConnectionHovers);
        _connectionHoversForCurrentFrame.Clear();

        if (!context.View.IsHovered)
            _lastConnectionHovers.Clear();

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
                ImGui.TextUnformatted("Click to insert operator or\ndrag to disconnect...");
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
            _evaluationContext.RequestedResolution = new Int2(1280 / 2, 720 / 2);
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