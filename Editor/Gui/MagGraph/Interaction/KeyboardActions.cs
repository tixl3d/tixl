#nullable enable
using T3.Editor.Gui.Graph.Interaction;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class KeyboardActions
{
    internal static ChangeSymbol.SymbolModificationResults HandleKeyboardActions(GraphUiContext context)
    {
        var result = ChangeSymbol.SymbolModificationResults.Nothing;
        
        var compositionOp = context.CompositionInstance;
        //var compositionUi = compositionOp.GetSymbolUi();

        if (KeyboardBinding.Triggered(UserActions.FocusSelection))
        {
            // TODO: Implement
            Log.Debug("Not implemented yet");
            context.Canvas.FocusViewToSelection(context);
        }

        if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.Duplicate))
        {
            NodeActions.CopySelectedNodesToClipboard(context.Selector, compositionOp);
            NodeActions.PasteClipboard(context.Selector, context.Canvas, compositionOp);
            context.Layout.FlagStructureAsChanged();
            
            result |= ChangeSymbol.SymbolModificationResults.StructureChanged;
        }

        if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.DeleteSelection)
                                    && context.Selector.Selection.Count > 0
                                    && context.StateMachine.CurrentState == GraphStates.Default)
        {
            result |= Modifications.DeleteSelection(context);
        }

        if (!T3Ui.IsCurrentlySaving 
            && KeyboardBinding.Triggered(UserActions.AlignSelectionLeft)
            && context.Selector.Selection.Count > 1
            && context.StateMachine.CurrentState == GraphStates.Default)
        {
            result |= Modifications.AlignSelectionToLeft(context);
        }
        
        if (KeyboardBinding.Triggered(UserActions.ToggleDisabled))
        {
            NodeActions.ToggleDisabledForSelectedElements(context.Selector);
        }

        if (KeyboardBinding.Triggered(UserActions.ToggleBypassed))
        {
            NodeActions.ToggleBypassedForSelectedElements(context.Selector);
        }

        if (KeyboardBinding.Triggered(UserActions.PinToOutputWindow))
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

        if (KeyboardBinding.Triggered(UserActions.DisplayImageAsBackground))
        {
            var selectedImage = context.Selector.GetFirstSelectedInstance();
            if (selectedImage != null)
            {
                // TODO: implement
                //_window.GraphImageBackground.OutputInstance = selectedImage;
                Log.Debug("Not implemented yet");
            }
        }

        if (KeyboardBinding.Triggered(UserActions.DisplayImageAsBackground))
        {
            var selectedImage = context.Selector.GetFirstSelectedInstance();
            if (selectedImage != null && ProjectView.Focused != null)
            {
                ProjectView.Focused.SetBackgroundOutput(selectedImage);
                //GraphWindow.Focused..SetBackgroundInstanceForCurrentGraph(selectedImage);
            }
        }

        if (KeyboardBinding.Triggered(UserActions.CopyToClipboard))
        {
            NodeActions.CopySelectedNodesToClipboard(context.Selector, compositionOp);
        }

        if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.PasteFromClipboard))
        {
            NodeActions.PasteClipboard(context.Selector, context.Canvas, compositionOp);
            context.Layout.FlagStructureAsChanged();
        }
        
        if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.PasteValues))
        {
            NodeActions.PasteValues(context.Selector, context.Canvas, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
        }

        // if (KeyboardBinding.Triggered(UserActions.LayoutSelection))
        // {
        //     _nodeGraphLayouting.ArrangeOps(compositionOp);
        // }

        if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.AddAnnotation))
        {
            var newAnnotation = NodeActions.AddAnnotation(context.Selector, context.Canvas, compositionOp);
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

        if (KeyboardBinding.Triggered(UserActions.AddComment))
        {
            context.EditCommentDialog.ShowNextFrame();
        }

        if (context.StateMachine.CurrentState == GraphStates.Default)
        {
            var oneSelected = context.Selector.Selection.Count == 1;
            if (oneSelected && KeyboardBinding.Triggered(UserActions.RenameChild))
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