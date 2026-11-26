#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using T3.Core.DataTypes;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Helpers;
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

    /// <summary>
    /// Adds the primary operator of the asset's type (e.g. LoadImage for an image) with its file parameter set to the asset.
    /// </summary>
    public static bool TryAddAssetOperator(Instance composition,
                                           Asset asset,
                                           Vector2 positionOnCanvas,
                                           [NotNullWhen(true)] out SymbolUi.Child? childUi,
                                           [NotNullWhen(true)] out Instance? newInstance)
    {
        childUi = null;
        newInstance = null;

        if (asset.AssetType.PrimaryOperators.Count == 0)
        {
            Log.Warning($"{asset.Address} of type {asset.AssetType} has no matching operator symbols");
            return false;
        }

        var symbolId = asset.AssetType.PrimaryOperators[0];
        if (!SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolUi))
        {
            Log.Warning($"Symbol {symbolId} not found in registry");
            return false;
        }

        if (!SymbolUiRegistry.TryGetSymbolUi(composition.Symbol.Id, out var compositionSymbolUi))
        {
            Log.Warning("Failed to get symbol ui for " + composition.SymbolChildId);
            return false;
        }

        childUi = AddSymbolChild(symbolUi.Symbol, compositionSymbolUi, positionOnCanvas);
        newInstance = composition.Children[childUi.Id];

        if (!SymbolAnalysis.TryGetFileInputFromInstance(newInstance, out var stringInput, out _))
        {
            Log.Warning("Failed to get file path parameter from op");
            return false;
        }

        Log.Debug($"Created {newInstance} with {asset.Address}", newInstance);

        stringInput.TypedInputValue.Assign(new InputValue<string>(asset.Address));
        stringInput.DirtyFlag.ForceInvalidate();
        composition.Symbol.InvalidateInputInAllChildInstances(stringInput);
        stringInput.Input.IsDefault = false;

        AssetRegistry.AddAssetReference(asset,
                                        symbolId: composition.Symbol.Id,
                                        stringInput.Parent.SymbolChildId,
                                        stringInput.Id);
        return true;
    }

    public static bool TryCopyNodesAsJson(Instance composition, 
                                          IEnumerable<SymbolUi.Child> selectedChildren, 
                                          List<Section> selectedSections, out string resultJsonString)
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
                                                selectedSections,
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