#nullable enable
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class KeyboardActions
{
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

        if (UserActions.PinToOutputWindow.Triggered())
        {
            if (LayoutHandling.FocusMode)
            {
                var selectedImage = nodeSelection.GetFirstSelectedInstance();
                if (selectedImage != null && ProjectView.Focused != null)
                {
                    ProjectView.Focused.SetBackgroundOutput(selectedImage);
                }
            }
            else
            {
                if (ProjectView.Focused != null)
                    NodeActions.PinSelectedToOutputWindow(ProjectView.Focused, 
                                                          nodeSelection, 
                                                          compositionOp, 
                                                          true);
            }
        }

        if (UserActions.DisplayImageAsBackground.Triggered())
        {
            var selectedImage = nodeSelection.GetFirstSelectedInstance();
            if (selectedImage != null && ProjectView.Focused != null)
            {
                ProjectView.Focused.SetBackgroundOutput(selectedImage);
            }
        }

        if (UserActions.CopyToClipboard.Triggered())
        {
            // Prevent node graph copy if a text input is active (e.g., section description)
            if (!ImGuiNET.ImGui.IsAnyItemActive())
            {
                NodeActions.CopySelectedNodesToClipboard(nodeSelection, compositionOp);
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
            NodeActions.PasteValues(nodeSelection, context.View, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
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

        // Todo: Implement
        // if (KeyboardBinding.Triggered(UserActions.SelectToAbove))
        // {
        //     NodeNavigation.SelectAbove();
        // }
        //
        // if (KeyboardBinding.Triggered(UserActions.SelectToRight))
        // {
        //     NodeNavigation.SelectRight();
        // }
        //
        // if (KeyboardBinding.Triggered(UserActions.SelectToLeft))
        // {
        //     NodeNavigation.SelectLeft();
        // }
        //
        // if (KeyboardBinding.Triggered(UserActions.SelectToBelow))
        // {
        //     NodeNavigation.SelectBelow();
        // }

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