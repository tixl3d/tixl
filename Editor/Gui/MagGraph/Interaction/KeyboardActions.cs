#nullable enable
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.UiHelpers;
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
            // TODO: Implement
            Log.Debug("Not implemented yet");
            context.View.FocusViewToSelection(context);
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.Duplicate.Triggered())
        {
            NodeActions.CopySelectedNodesToClipboard(context.Selector, compositionOp);
            NodeActions.PasteClipboard(context.Selector, context.View, compositionOp);
            context.Layout.FlagStructureAsChanged();

            result |= ChangeSymbol.SymbolModificationResults.StructureChanged;
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.DeleteSelection.Triggered()
                                    && context.Selector.Selection.Count > 0
                                    && context.StateMachine.CurrentState == GraphStates.Default)
        {
            result |= Modifications.DeleteSelection(context);
        }

        if (!T3Ui.IsCurrentlySaving
            && UserActions.AlignSelectionLeft.Triggered()
            && context.Selector.Selection.Count > 1
            && context.StateMachine.CurrentState == GraphStates.Default)
        {
            result |= Modifications.AlignSelectionToLeft(context);
        }

        if (UserActions.ToggleDisabled.Triggered())
        {
            NodeActions.ToggleDisabledForSelectedElements(context.Selector);
        }

        if (UserActions.ToggleBypassed.Triggered())
        {
            NodeActions.ToggleBypassedForSelectedElements(context.Selector);
        }

        // Navigation backwards / forward
        {
            IReadOnlyList<Guid>? navigationPath = null;

            if (UserActions.NavigateBackwards.Triggered())
                navigationPath = context.Selector.NavigationHistory.NavigateBackwards();

            if (UserActions.NavigateForward.Triggered())
                navigationPath = context.Selector.NavigationHistory.NavigateForward();

            if (navigationPath != null && context.View is IGraphView view)
                view.OpenAndFocusInstance(navigationPath);
        }

        if (UserActions.PinToOutputWindow.Triggered())
        {
            if (UserSettings.Config.FocusMode)
            {
                var selectedImage = context.Selector.GetFirstSelectedInstance();
                if (selectedImage != null && ProjectView.Focused != null)
                {
                    ProjectView.Focused.SetBackgroundOutput(selectedImage);
                }
            }
            else
            {
                // FIXME: This is a work around that needs a legacy graph window to be active
                if (ProjectView.Focused != null)
                    NodeActions.PinSelectedToOutputWindow(ProjectView.Focused, context.Selector, compositionOp);
            }
        }

        if (UserActions.DisplayImageAsBackground.Triggered())
        {
            var selectedImage = context.Selector.GetFirstSelectedInstance();
            if (selectedImage != null && ProjectView.Focused != null)
            {
                ProjectView.Focused.SetBackgroundOutput(selectedImage);
            }
        }

        if (UserActions.CopyToClipboard.Triggered())
        {
            NodeActions.CopySelectedNodesToClipboard(context.Selector, compositionOp);
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.PasteFromClipboard.Triggered())
        {
            NodeActions.PasteClipboard(context.Selector, context.View, compositionOp);
            context.Layout.FlagStructureAsChanged();
        }

        if (!T3Ui.IsCurrentlySaving && UserActions.PasteValues.Triggered())
        {
            NodeActions.PasteValues(context.Selector, context.View, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
        }

        // if (KeyboardBinding.Triggered(UserActions.LayoutSelection))
        // {
        //     _nodeGraphLayouting.ArrangeOps(compositionOp);
        // }

        if (!T3Ui.IsCurrentlySaving && UserActions.AddAnnotation.Triggered())
        {
            var newAnnotation = NodeActions.AddAnnotation(context.Selector, context.View, compositionOp);
            context.ActiveAnnotationId = newAnnotation.Id;
            context.StateMachine.SetState(GraphStates.RenameAnnotation, context);
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
            var oneSelected = context.Selector.Selection.Count == 1;
            if (oneSelected && UserActions.RenameChild.Triggered())
            {
                if (context.Layout.Items.TryGetValue(context.Selector.Selection[0].Id, out var item)
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