#nullable enable
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Interaction;

internal static class GraphCanvasUtils
{
    internal static void SelectAndCenterChildIdInView(Guid symbolChildId)
    {
        var components = ProjectView.Focused;
        if (components == null || components.CompositionInstance == null)
            return;

        var compositionOp = components.CompositionInstance;

        var symbolUi = compositionOp.GetSymbolUi();

        if (!symbolUi.ChildUis.TryGetValue(symbolChildId, out var sourceChildUi))
            return;

        if (!compositionOp.Children.TryGetChildInstance(symbolChildId, out var selectionTargetInstance))
            return;

        components.NodeSelection.SetSelection(sourceChildUi, selectionTargetInstance);
        FitViewToSelectionHandling.FitViewToSelection();
    }
}