using T3.Core.SystemUi;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Templates;

/// <summary>
/// Handles the creation of symbols from <see cref="TemplateDefinition"/> 
/// </summary>
public static class TemplateUse
{
    internal static void TryToApplyTemplate(TemplateDefinition template, string symbolName, string nameSpace, string description, EditableSymbolProject project)
    {
        var components = ProjectView.Focused;
        if (components == null || components.CompositionInstance == null)
        {
            BlockingWindow.Instance.ShowMessageBox("Can't create from template without open graph window");
            return;
        }

        var compositionSymbolUi = components.CompositionInstance.GetSymbolUi();

        var graphCanvas = components.GraphView;
        var centerOnScreen = graphCanvas.Canvas.WindowPos + graphCanvas.Canvas.WindowSize / 2;
        var positionOnCanvas2 = graphCanvas.Canvas.InverseTransformPositionFloat(centerOnScreen);
        var freePosition = FindFreePositionOnCanvas(compositionSymbolUi, positionOnCanvas2);
        var newSymbol = Duplicate.DuplicateAsNewType(compositionSymbolUi, project, template.TemplateSymbolId, symbolName, nameSpace, description, freePosition);
            
        // Select instance of new symbol
        if (newSymbol == null || !compositionSymbolUi.ChildUis.TryGetValue(newSymbol.Id, out var newChildUi))
        {
            Log.Debug("Creating symbol for template failed. Couldn't find child ui");
            return;
        }
        
        GraphCanvasUtils.SelectAndCenterChildIdInView(newChildUi.SymbolChild.Id);
        var newInstance = components.NodeSelection.GetSelectedInstanceWithoutComposition(); 
        template.AfterSetupAction?.Invoke(newInstance,
                                          symbolName,
                                          nameSpace, 
                                          description, 
                                          project.AssetsFolder);
        T3Ui.Save(false);
    }

    private static Vector2 FindFreePositionOnCanvas(SymbolUi compositionSymbolUi, Vector2 pos)
    {
        while (true)
        {
            var isPositionFree = true;
            foreach (var childUi in compositionSymbolUi.ChildUis.Values)
            {
                var rect = ImRect.RectWithSize(childUi.PosOnCanvas, childUi.Size);
                rect.Expand(20);
                if (!rect.Contains(pos))
                    continue;

                pos.X += childUi.Size.X;
                isPositionFree = false;
                break;
            }

            if (isPositionFree)
                return pos;
        }
    }
}