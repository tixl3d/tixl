#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Handles snapping to input and output connections
/// </summary>
internal static class OutputSnapper
{
    public static void Update(GraphUiContext context)
    {
        BestOutputMatch = _bestOutputMatchForCurrentFrame;
        _bestOutputMatchForCurrentFrame = new OutputMatch();
        
        if (context.StateMachine.CurrentState != GraphStates.HoldingConnectionBeginning)
            return;
        
        if (BestOutputMatch.Item != null)
        {
            ImGui.GetWindowDrawList().AddCircle(context.Canvas.TransformPosition(BestOutputMatch.Anchor.PositionOnCanvas), 20, Color.Red);
        }
    }

    public static void RegisterAsPotentialTargetOutput(GraphUiContext context, MagGraphItem item, MagGraphItem.OutputAnchorPoint outputAnchor)
    {
        var posOnScreen = context.Canvas.TransformPosition(outputAnchor.PositionOnCanvas);
        var distance = Vector2.Distance(posOnScreen, ImGui.GetMousePos());
        
        if (distance < _bestOutputMatchForCurrentFrame.Distance)
        {
            _bestOutputMatchForCurrentFrame = new OutputMatch(item, outputAnchor, distance);
        }
    }

    public static bool TryToReconnect(GraphUiContext context)
    {
        if (BestOutputMatch.Item == null)
            return false;

        if (context.TempConnections.Count == 0)
            return false;

        Debug.Assert(context.MacroCommand != null);

        var didSomething = false;
        foreach (var tempConnection in context.TempConnections.OrderBy(t => t.TargetInput.Id).ThenBy(t=> t.MultiInputIndex))
        {
            var sourceParentOrChildId = BestOutputMatch.Item.Variant == MagGraphItem.Variants.Input ? Guid.Empty : BestOutputMatch.Item.Id;
            var targetParentOrChildId = tempConnection.TargetItem.Variant == MagGraphItem.Variants.Output ? Guid.Empty : tempConnection.TargetItem.Id;
            
            // Create connection
            var connectionToAdd = new Symbol.Connection(sourceParentOrChildId,
                                                        BestOutputMatch.Anchor.SlotId,
                                                        targetParentOrChildId,
                                                        tempConnection.TargetInput.Id);

            if (Structure.CheckForCycle(context.CompositionInstance.Symbol, connectionToAdd))
            {
                Log.Debug("This action is not allowed. This connection would create a cycle.");
                continue;
            }
            
            context.MacroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                            connectionToAdd,
                                                                            tempConnection.MultiInputIndex));
            didSomething = true;
        }

        return didSomething;
    }

    public const float SnapThreshold = 30;

    public sealed record OutputMatch(MagGraphItem? Item = null, MagGraphItem.OutputAnchorPoint Anchor = default, float Distance = SnapThreshold);

    private static OutputMatch _bestOutputMatchForCurrentFrame = new();
    public static OutputMatch BestOutputMatch = new();
}