#nullable enable
using System.IO;
using Newtonsoft.Json;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using Vector2 = System.Numerics.Vector2;

// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator

namespace T3.Editor.UiModel.Modification;

internal static class GraphOperations
{
    public static SymbolUi.Child AddSymbolChild(Symbol symbol, SymbolUi parentUi, Vector2 positionOnCanvas)
    {
        var addCommand = new AddSymbolChildCommand(parentUi.Symbol, symbol.Id) { PosOnCanvas = positionOnCanvas };
        UndoRedoStack.AddAndExecute(addCommand);
            
        var parentSymbol = parentUi.Symbol;
        var newSymbolChild = parentSymbol.Children[addCommand.AddedChildId];

        // Select new node
        return newSymbolChild.GetChildUi();
    }

    public static bool TryCopyNodesAsJson(Instance composition, 
                                          IEnumerable<SymbolUi.Child> selectedChildren, 
                                          List<Annotation> selectedAnnotations, out string resultJsonString)
    {
        resultJsonString = string.Empty;
        
        if (ProjectView.Focused == null)
            return false;
        
        var package = ProjectView.Focused.OpenedProject.Package;
        if (!package.TryCreateNewSymbol<object>(out var newContainerUi))
        {
            Log.Error($"Failed to copy nodes to clipboard. Could not create new symbol.");
            return false;
        }
            
        var cmd = new CopySymbolChildrenCommand(composition.GetSymbolUi(),
                                                selectedChildren.ToList(),
                                                selectedAnnotations,
                                                newContainerUi,
                                                Vector2.Zero,
                                                copyMode: CopySymbolChildrenCommand.CopyMode.ClipboardTarget);
        cmd.Do();
            
        using (var writer = new StringWriter())
        {
            var jsonWriter = new JsonTextWriter(writer);
            jsonWriter.WriteStartArray();
            SymbolJson.WriteSymbol(newContainerUi.Symbol, jsonWriter);
            SymbolUiJson.WriteSymbolUi(newContainerUi, jsonWriter);
            jsonWriter.WriteEndArray();
                
            try
            {
                resultJsonString = writer.ToString();
            }
            catch (Exception)
            {
                Log.Error("Could not copy elements to clipboard. Perhaps a tool like TeamViewer locks it.");
                    
                // remove symbol from package as it is only temporary
                package.RemoveSymbolUi(newContainerUi);
                return false;
            }
        }
            
        package.RemoveSymbolUi(newContainerUi);
        return true;
    }
}