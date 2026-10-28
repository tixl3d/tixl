#nullable enable
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class KeyboardActions
{
    private const string DeleteKeepsConnectionsTipId = "DeleteKeepsConnections";

    internal static ChangeSymbol.SymbolModificationResults HandleKeyboardActions(GraphUiContext context)
    {
        var result = ChangeSymbol.SymbolModificationResults.Nothing;

        var compositionOp = context.CompositionInstance;
        //var compositionUi = compositionOp.GetSymbolUi();

        if (UserActions.FocusSelection.Triggered())
        {
            context.ProjectView.FocusViewToSelection();
        }

        var nodeSelection = context.Selector;
        if (!T3Ui.IsCurrentlySaving && UserActions.Duplicate.Triggered())
        {
            // Only paste if something was actually copied - otherwise duplicating an
            // input node would paste stale clipboard content.
            if (NodeActions.CopySelectedNodesToClipboard(nodeSelection, compositionOp))
            {
                NodeActions.PasteClipboard(nodeSelection, context.View, compositionOp);
                context.Layout.FlagStructureAsChanged();

                result |= ChangeSymbol.SymbolModificationResults.StructureChanged;
            }
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.DuplicateWithConnections.Triggered())
        {
            Modifications.DuplicateWithConnections(context);
            context.Layout.FlagStructureAsChanged();

            result |= ChangeSymbol.SymbolModificationResults.StructureChanged;
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.DeleteSelection.Triggered()
                                    && nodeSelection.Selection.Count > 0
                                    && context.StateMachine.CurrentState == GraphStates.Default)
        {
            result |= Modifications.DeleteSelection(context);
            ActionToast.ShowTip(DeleteKeepsConnectionsTipId, "{Deleted, connections were kept}   [" + KeyCombination.CtrlLabel + "+Del] also disconnects");
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.DeleteSelectionAndConnections.Triggered()
                                    && nodeSelection.Selection.Count > 0
                                    && context.StateMachine.CurrentState == GraphStates.Default)
        {
            ActionToast.MarkLearned(DeleteKeepsConnectionsTipId);
            result |= Modifications.DeleteSelection(context, keepConnections: false);
        }

        if (!T3Ui.IsCurrentlySaving
            && UserActions.AlignSelectionLeft.Triggered()
            && nodeSelection.Selection.Count > 1
            && context.StateMachine.CurrentState == GraphStates.Default)
        {
            result |= Modifications.AlignSelectionToLeft(context);
        }

        if (UserActions.ToggleDisabled.Triggered())
        {
            NodeActions.ToggleDisabledForSelectedElements(nodeSelection);
        }

        if (UserActions.ToggleBypassed.Triggered())
        {
            NodeActions.ToggleBypassedForSelectedElements(nodeSelection);
        }

        if (UserActions.Disconnect.Triggered())
        {
            NodeActions.DisconnectNodes(context.CompositionInstance, nodeSelection.Selection.ToList());
            context.Layout.FlagStructureAsChanged();
        }

        // Navigation backwards / forward
        {
            IReadOnlyList<Guid>? navigationPath = null;

            if (UserActions.NavigateBackwards.Triggered())
                navigationPath = nodeSelection.NavigationHistory.NavigateBackwards();

            if (UserActions.NavigateForward.Triggered())
                navigationPath = nodeSelection.NavigationHistory.NavigateForward();

            if (navigationPath != null && context.View is IGraphView view)
                view.OpenAndFocusInstance(navigationPath);
        }

        OutputPeek.Handle(context);

        if (UserActions.CopyToClipboard.Triggered())
        {
            // Prevent node graph copy if a text input is active (e.g., section description)
            if (!ImGuiNET.ImGui.IsAnyItemActive())
            {
                var copiedCount = nodeSelection.Selection.Count;
                if (NodeActions.CopySelectedNodesToClipboard(nodeSelection, compositionOp))
                    ActionToast.Show("{Copied " + copiedCount + (copiedCount == 1 ? " operator}" : " operators}") + "   paste as text into a message to share");
            }
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.PasteFromClipboard.Triggered())
        {
            // Prevent node graph paste if a text input is active (e.g., section description)
            if (!ImGuiNET.ImGui.IsAnyItemActive())
            {
                NodeActions.PasteClipboard(nodeSelection, context.View, compositionOp);
                context.Layout.FlagStructureAsChanged();
            }
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.PasteValues.Triggered())
        {
            var pastedCount = NodeActions.PasteValues(nodeSelection, context.View, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
            if (pastedCount > 0)
                ActionToast.Show("{Pasted " + pastedCount + (pastedCount == 1 ? " value}" : " values}"));
        }

        if (!T3Ui.IsCurrentlySaving
            && UserActions.LayoutSelection.Triggered()
            && nodeSelection.Selection.Count > 0
            && context.StateMachine.CurrentState == GraphStates.Default)
        {
            TreeLayouting.LayoutInputsOfSelection(context);
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.AddSection.Triggered())
        {
            var newSection = NodeActions.AddSection(nodeSelection, context.View, compositionOp);
            context.ActiveSectionId = newSection.Id;
            context.StateMachine.SetState(GraphStates.RenameSection, context);
            context.Layout.FlagStructureAsChanged();
        }

        //IReadOnlyList<Guid>? navigationPath = null;

        // Navigation (this should eventually be part of the graph window)
        // if (KeyboardBinding.Triggered(UserActions.NavigateBackwards))
        // {
        //     navigationPath = context.NavigationHistory.NavigateBackwards();
        // }
        //
        // if (KeyboardBinding.Triggered(UserActions.NavigateForward))
        // {
        //     navigationPath = context.NavigationHistory.NavigateForward();
        // }

        //if (navigationPath != null)
        //    _window.TrySetCompositionOp(navigationPath);

        if (context.StateMachine.CurrentState == GraphStates.Default)
        {
            if (UserActions.SelectToAbove.Triggered())
                SelectionNavigation.MoveSelection(context, SelectionNavigation.Directions.Up);

            if (UserActions.SelectToRight.Triggered())
                SelectionNavigation.MoveSelection(context, SelectionNavigation.Directions.Right);

            if (UserActions.SelectToBelow.Triggered())
                SelectionNavigation.MoveSelection(context, SelectionNavigation.Directions.Down);

            if (UserActions.SelectToLeft.Triggered())
                SelectionNavigation.MoveSelection(context, SelectionNavigation.Directions.Left);

            if (UserActions.EditParameter.Triggered())
                ParameterPopover.Open(context);

            OperatorBookmarks.HandleKeyboard(context);
        }

        if (UserActions.AddComment.Triggered())
        {
            context.EditCommentDialog.ShowNextFrame();
        }

        if (context.StateMachine.CurrentState == GraphStates.Default)
        {
            var oneSelected = nodeSelection.Selection.Count == 1;
            if (oneSelected && UserActions.RenameChild.Triggered())
            {
                if (context.Layout.Items.TryGetValue(nodeSelection.Selection[0].Id, out var item)
                    && item.Variant == MagGraphItem.Variants.Operator)
                {
                    RenamingOperator.OpenForChildUi(item.ChildUi!);
                    context.StateMachine.SetState(GraphStates.RenameChild, context);
                }
            }
        }

        return result;
    }
}