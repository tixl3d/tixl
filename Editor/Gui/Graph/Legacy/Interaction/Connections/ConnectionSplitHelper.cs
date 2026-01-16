#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.TransformGizmos;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Legacy.Interaction.Connections;

internal static class ConnectionSplitHelper
{
    public static void PrepareNewFrame(ProjectView components)
    {
        _mousePosition = ImGui.GetMousePos();
        BestMatchLastFrame = _bestMatchYetForCurrentFrame;
        if (components.GraphView is not GraphView graphCanvas)
            return;
            
        if (BestMatchLastFrame != null 
            && !ConnectionMaker.HasTempConnectionsFor(graphCanvas))
        {
            var time = ImGui.GetTime();
            if (_hoverStartTime < 0)
                _hoverStartTime = time;

            var hoverDuration = time - _hoverStartTime;
            var radius = EaseFunctions.EaseOutElastic((float)hoverDuration) * 4;
            var drawList = ImGui.GetForegroundDrawList();

            drawList.AddCircleFilled(BestMatchLastFrame.PositionOnScreen, radius, BestMatchLastFrame.Color, 30);

            var buttonMin = _mousePosition - Vector2.One * radius / 2;
            ImGui.SetCursorScreenPos(buttonMin);

            if (ImGui.InvisibleButton("splitMe", Vector2.One * radius))
            {
                var posOnScreen = graphCanvas.InverseTransformPositionFloat(BestMatchLastFrame.PositionOnScreen)
                                  - new Vector2(SymbolUi.Child.DefaultOpSize.X * 0.25f,
                                                SymbolUi.Child.DefaultOpSize.Y * 0.5f);

                ConnectionMaker.SplitConnectionWithSymbolBrowser(components, components.CompositionInstance!.Symbol,
                                                                 BestMatchLastFrame.Connection,
                                                                 posOnScreen, graphCanvas.SymbolBrowser);
            }

            ImGui.BeginTooltip();
            {
                var connection = BestMatchLastFrame.Connection;

                ISlot? outputSlot = null;
                Symbol.Child.Output? output = null;
                Symbol.OutputDefinition? outputDefinition;

                var op = components.CompositionInstance!;

                Symbol.Child? sourceOp = null;
                if (op.Children.TryGetChildInstance(connection.SourceParentOrChildId, out var sourceOpInstance))
                {
                    sourceOp = sourceOpInstance.SymbolChild;
                    outputDefinition = sourceOpInstance.Symbol.OutputDefinitions.SingleOrDefault(outDef => outDef.Id == connection.SourceSlotId);
                    if (outputDefinition != null && sourceOp != null)
                    {
                        output = sourceOp.Outputs[connection.SourceSlotId];
                        outputSlot = sourceOpInstance.Outputs.Single(slot => slot.Id == outputDefinition.Id);
                    }
                }

                Symbol.Child.Input? input = null;
                if (op.Symbol.Children.TryGetValue(connection.TargetParentOrChildId, out var targetOp))
                {
                    input = targetOp.Inputs[connection.TargetSlotId];
                }

                if (outputSlot != null && output != null && input != null)
                {

                    var width = 160f;
                    ImGui.SetNextWindowSizeConstraints(new Vector2(200, 200*9/16f), new Vector2(200, 200*9/16f));

                    ImGui.BeginChild("thumbnail", new Vector2(width, width * 9 / 16f));
                    {
                            
                        TransformGizmoHandling.SetDrawList(drawList);
                        _imageCanvasForTooltips.Update();
                        _imageCanvasForTooltips.SetAsCurrent();

                        //var sourceOpUi = SymbolUiRegistry.Entries[graphCanvas.CompositionOp.Symbol.Id].ChildUis.Single(childUi => childUi.Id == sourceOp.Id);
                        if (sourceOpInstance != null)
                        {
                            var sourceOpUi = sourceOpInstance.GetSymbolUi();
                            var outputUi = sourceOpUi.OutputUis[output.OutputDefinition.Id];
                            _evaluationContext.Reset();
                            _evaluationContext.RequestedResolution = new Int2(1280 / 2, 720 / 2);
                            outputUi.DrawValue(outputSlot, 
                                               _evaluationContext,
                                               "connectionLineThumbnail",
                                               recompute: UserSettings.Config.HoverMode == UserSettings.GraphHoverModes.Live);
                        }

                        // if (!string.IsNullOrEmpty(sourceOpUi.Description))
                        // {
                        //     ImGui.Spacing();
                        //     ImGui.PushFont(Fonts.FontSmall);
                        //     ImGui.PushStyleColor(ImGuiCol.Text, new Color(1, 1, 1, 0.5f).Rgba);
                        //     ImGui.TextWrapped(sourceOpUi.Description);
                        //     ImGui.PopStyleColor();
                        //     ImGui.PopFont();
                        // }

                        ImageOutputCanvas.Deactivate();
                        TransformGizmoHandling.RestoreDrawList();
                    }
                    ImGui.EndChild();

                    if (sourceOp != null && targetOp != null)
                    {
                        ImGui.PushFont(Fonts.FontSmall);
                        var type = output.OutputDefinition.ValueType;
                        var connectionSource = sourceOp.ReadableName + "." + output.OutputDefinition.Name;
                        ImGui.TextColored(UiColors.TextMuted, connectionSource);
                                
                        ImGui.TextUnformatted(type.Name);
                        ImGui.SameLine();
                        var connectionTarget = "--> " + targetOp.ReadableName + "." + input.Name;
                        ImGui.TextColored(UiColors.TextMuted, connectionTarget);
                        ImGui.PopFont();
                        
                        var nodeSelection = components.NodeSelection;
                        FrameStats.AddHoveredId(targetOp.Id);
                        FrameStats.AddHoveredId(sourceOp.Id);
                    }
                }
            }
            ImGui.EndTooltip();
        }
        else
        {
            _hoverStartTime = -1;
        }

        _bestMatchYetForCurrentFrame = null;
        _bestMatchDistance = float.PositiveInfinity;
    }
    

    public static void RegisterAsPotentialSplit(Symbol.Connection connection, Color color, Vector2 position)
    {
        var distance = Vector2.Distance(position, _mousePosition);
        if (distance > SnapDistance || distance > _bestMatchDistance)
        {
            return;
        }

        _bestMatchYetForCurrentFrame = new PotentialConnectionSplit()
                                           {
                                               Connection = connection,
                                               PositionOnScreen = position,
                                               Color = color,
                                           };
        _bestMatchDistance = distance;
    }

    private static readonly ImageOutputCanvas _imageCanvasForTooltips = new() { DisableDamping = true };
    private static readonly EvaluationContext _evaluationContext = new();

    public static PotentialConnectionSplit? BestMatchLastFrame;
    private static PotentialConnectionSplit? _bestMatchYetForCurrentFrame;
    private static float _bestMatchDistance = float.PositiveInfinity;
    private const int SnapDistance = 50;
    private static Vector2 _mousePosition;
    private static double _hoverStartTime = -1;

    public sealed class PotentialConnectionSplit
    {
        public Vector2 PositionOnScreen;
        public required Symbol.Connection Connection;
        public Color Color;
    }
}