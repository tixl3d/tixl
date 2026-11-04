#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction.Gamepad;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Moves the snapped group of the selected operator with a game controller: holding the right stick down grabs the
/// whole group, tilting it moves the group, releasing the stick puts it down as one undo step. Unlike a mouse drag
/// it never splits the group, splices into connections or snaps to other operators.
/// </summary>
internal static class GamepadGroupDrag
{
    internal static void Update(GraphUiContext context)
    {
        var isGrabbing = context.ProjectView == ProjectView.Focused
                         && context.StateMachine.CurrentState == GraphStates.Default
                         && GamepadControl.IsGrabbingGroup;

        if (!isGrabbing)
        {
            Complete(context);
            return;
        }

        if (_moveCommand == null && !TryStart(context))
            return;

        var stick = GamepadControl.GroupDragStick;
        if (stick == Vector2.Zero)
            return;

        var speedPx = GamepadControl.MaxPanSpeedPx * T3Ui.UiScaleFactor * stick.Length() * ImGui.GetIO().DeltaTime;
        var offsetOnCanvas = new Vector2(stick.X, -stick.Y) * speedPx / context.View.Scale;
        foreach (var movable in _movedItems)
        {
            movable.PosOnCanvas += offsetOnCanvas;
        }

        _hasMoved = true;
        context.Layout.FlagStructureAsChanged();
    }

    private static bool TryStart(GraphUiContext context)
    {
        if (!GraphStates.TryGetSingleSelectedItem(context, out var item))
            return false;

        _movedItems.Clear();
        foreach (var snappedItem in MagItemMovement.CollectSnappedItems(item))
        {
            _movedItems.Add(snappedItem);
        }

        _moveCommand = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, _movedItems, context.Selector);
        _hasMoved = false;
        return true;
    }

    private static void Complete(GraphUiContext context)
    {
        if (_moveCommand == null)
            return;

        if (_hasMoved)
        {
            _moveCommand.StoreCurrentValues();
            UndoRedoStack.Add(_moveCommand);
            context.Layout.FlagStructureAsChanged();
        }

        _moveCommand = null;
        _movedItems.Clear();
    }

    private static ModifyCanvasElementsCommand? _moveCommand;
    private static bool _hasMoved;
    private static readonly List<ISelectableCanvasObject> _movedItems = [];
}
