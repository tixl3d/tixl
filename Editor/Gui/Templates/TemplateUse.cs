using T3.Core.SystemUi;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
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

        var freePosition = GraphUtils.FindFreePositionInView(compositionSymbolUi);
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
}