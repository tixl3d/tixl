#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Handles snapping to input and output connections
/// </summary>
internal static class InputSnapper
{
    public static void Update(GraphUiContext context)
    {
        BestInputMatch = _bestInputMatchForCurrentFrame;
        _bestInputMatchForCurrentFrame = new InputMatch();

        if (context.StateMachine.CurrentState != GraphStates.HoldingConnectionEnd)
            return;

        if (BestInputMatch.Item != null)
        {
            // TODO: Make beautiful
            ImGui.GetWindowDrawList().AddCircle(context.View.TransformPosition(BestInputMatch.PosOnScreen), 20, Color.Red);
        }
    }

    public static void RegisterAsPotentialTargetInput(MagGraphItem item, Vector2 posOnScreen, Guid slotId,
                                                      InputSnapTypes inputSnapType = InputSnapTypes.Normal, int multiInputIndex = 0)
    {
        var mousePos = ImGui.GetMousePos();
        var distance = Vector2.Distance(posOnScreen, mousePos);
        if ( distance < _bestInputMatchForCurrentFrame.Distance)
        {
            _bestInputMatchForCurrentFrame = new InputMatch(item, slotId, posOnScreen, inputSnapType, multiInputIndex, distance);
        }
    }

    public static bool TryToReconnect(GraphUiContext context)
    {
        if (BestInputMatch.Item == null)
            return false;

        if (context.TempConnections.Count == 0)
            return false;

        if (context.MacroCommand == null)
        {
            context.StartMacroCommand("Create connection");
        }

        //Debug.Assert(context.MacroCommand != null);

        Debug.Assert(context.TempConnections.Count == 1);

        var tempConnection = context.TempConnections[0];

        // TODO: Use snap type...
        // Create connection
        var sourceParentOrChildId = tempConnection.SourceItem.Variant == MagGraphItem.Variants.Input ? Guid.Empty : tempConnection.SourceItem.Id;
        var targetParentOrChildId = BestInputMatch.Item.Variant == MagGraphItem.Variants.Output ? Guid.Empty : BestInputMatch.Item.Id;

        var existingConnection = context.CompositionInstance.Symbol.Connections.FirstOrDefault(c => c.TargetParentOrChildId == targetParentOrChildId
                                                                                                    && c.TargetSlotId == BestInputMatch.SlotId);
        if (existingConnection != null && (BestInputMatch.InputSnapType == InputSnapTypes.Normal || BestInputMatch.InputSnapType == InputSnapTypes.ReplaceMultiInput))
        {
            context.MacroCommand?.AddAndExecCommand(new DeleteConnectionCommand(context.CompositionInstance.Symbol, existingConnection, BestInputMatch.MultiInputIndex));
        }
        
        var connectionToAdd = new Symbol.Connection(sourceParentOrChildId,
                                                    tempConnection.SourceOutput.Id,
                                                    targetParentOrChildId,
                                                    BestInputMatch.SlotId);

        if (Structure.CheckForCycle(context.CompositionInstance.Symbol, connectionToAdd))
        {
            Log.Debug("This action is not allowed. This connection would create a cycle.");
            return false;
        }

        var multiInputIndex = BestInputMatch.MultiInputIndex;
        var adjustedMultiInputIndex = multiInputIndex + (BestInputMatch.InputSnapType == InputSnapTypes.InsertAfterMultiInput ? 1 : 0);
        
        
        
        
        if (BestInputMatch.InputSnapType == InputSnapTypes.ReplaceMultiInput)
        {
            // context.MacroCommand!.AddAndExecCommand(new DeleteConnectionCommand(context.CompositionInstance.Symbol,
            //                                                                     connectionToAdd,
            //                                                                     adjustedMultiInputIndex));

            context.MacroCommand!.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                             connectionToAdd,
                                                                             adjustedMultiInputIndex));
        }
        else
        {
            context.MacroCommand!.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                             connectionToAdd,
                                                                             adjustedMultiInputIndex));
        }

        
            
        
        // Push down other items
        {
            var lines = BestInputMatch.Item.InputLines;
            var inputLineIndex = 0;
            while ( inputLineIndex < lines.Length && lines[inputLineIndex].Id != BestInputMatch.SlotId)
            {
                inputLineIndex++;
            }

            var wasInputLineWasConnected = lines[inputLineIndex].ConnectionIn != null;

            var hasHeightChanged = (BestInputMatch.InputSnapType == InputSnapTypes.InsertAfterMultiInput ||
                                    BestInputMatch.InputSnapType == InputSnapTypes.InsertBeforeMultiInput)
                                   || (BestInputMatch.InputSnapType == InputSnapTypes.Normal 
                                       && !wasInputLineWasConnected 
                                       && lines[inputLineIndex].InputUi.Relevancy == Relevancy.Optional
                                       && inputLineIndex > 0);
            
            if (  hasHeightChanged && inputLineIndex < lines.Length)
            {
                var insertionIndex = inputLineIndex + adjustedMultiInputIndex;
                var collectSnappedItems = MagItemMovement.CollectSnappedItems(BestInputMatch.Item);
                collectSnappedItems.Remove(BestInputMatch.Item);
                MagItemMovement.MoveSnappedItemsVertically(context,
                                                           collectSnappedItems,
                                                           BestInputMatch.Item.PosOnCanvas.Y + MagGraphItem.GridSize.Y * (insertionIndex - 0.5f),
                                                           MagGraphItem.GridSize.Y);
            }
        }
        return true;
    }

    public enum InputSnapTypes
    {
        Normal,
        InsertBeforeMultiInput,
        ReplaceMultiInput,
        InsertAfterMultiInput,
    }

    public sealed record InputMatch(
        MagGraphItem? Item = null,
        Guid SlotId = default,
        Vector2 PosOnScreen = default,
        InputSnapTypes InputSnapType = InputSnapTypes.Normal,
        int MultiInputIndex = 0,
        float Distance = OutputSnapper.SnapThreshold);

    private static InputMatch _bestInputMatchForCurrentFrame = new();
    public static InputMatch BestInputMatch = new();
}