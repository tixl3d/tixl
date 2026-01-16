#nullable enable
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Interaction;

/// <summary>
/// Breaking out parameters into connected value operators with original value and renamed to parameter named.
/// </summary>
internal static class ParameterExtraction
{
    public static bool IsInputSlotExtractable(IInputSlot inputSlot)
    {
        return SymbolsExtractableFromInputs.ContainsKey(inputSlot.ValueType);
    }
    
    public static void ExtractAsConnectedOperator<T>(InputSlot<T> inputSlot, SymbolUi.Child symbolChildUi, Symbol.Child.Input input, Vector2 freePosition)
    {
        var view = ProjectView.Focused;
        if (view?.InstView == null)
        {
            Log.Warning("Unable to access current view for extractions?");
            return;
        }
        
        var nodeSelection = view.NodeSelection;
        var compositionUi = view.InstView.SymbolUi;
        var compositionInstance = view.InstView.Instance;
        
        var commands = new List<ICommand>();

        // cast input slot to constructedInputSlotType
        // Find matching symbol
        if (!SymbolsExtractableFromInputs.TryGetValue(input.DefaultValue.ValueType, out var symbolId))
        {
            Log.Warning("Can't extract this parameter type");
            return;
        }

        // Add Child
        var addSymbolChildCommand = new AddSymbolChildCommand(compositionUi.Symbol, symbolId)
                                        {
                                            PosOnCanvas = freePosition,
                                            ChildName = input.Name
                                        };
        
        if (_sizesForTypes.TryGetValue(input.DefaultValue.ValueType, out var sizeOverride))
        {
            addSymbolChildCommand.Size = sizeOverride;  // FIXME: doesn't seem to have an effect
        }

        commands.Add(addSymbolChildCommand);
        addSymbolChildCommand.Do();
        
        var newChildUi = compositionUi.ChildUis[addSymbolChildCommand.AddedChildId];
        var newSymbolChild = newChildUi.SymbolChild;

        // Sadly, we have to apply size manually.
        if (_sizesForTypes.TryGetValue(input.DefaultValue.ValueType, out _))
        {
            newChildUi.Style = SymbolUi.Child.Styles.Resizable;
        }

        // Set type
        // Todo - make this undoable - currently not implemented with the new extraction system
        var newInstance = compositionInstance.Children[newChildUi.Id];
        ExtractInputValues(inputSlot, newInstance, out var outputSlot);

        // Create connection
        var newConnection = new Symbol.Connection(sourceParentOrChildId: newSymbolChild.Id,
                                                  sourceSlotId: outputSlot.Id,
                                                  targetParentOrChildId: symbolChildUi.SymbolChild.Id,
                                                  targetSlotId: input.Id);
        var addConnectionCommand = new AddConnectionCommand(compositionUi.Symbol, newConnection, 0);
        addConnectionCommand.Do();
        commands.Add(addConnectionCommand);
        UndoRedoStack.Add(new MacroCommand("Extract as operator", commands));
        return;

        static void ExtractInputValues(InputSlot<T> slot, Instance newInstance, out Slot<T> outputSlot)
        {
            var extractableInput = (IExtractedInput<T>)newInstance;
            outputSlot = extractableInput.OutputSlot;
            extractableInput.SetTypedInputValuesTo(slot.TypedInputValue.Value, out var changedSlots);
            foreach (var changedSlot in changedSlots)
            {
                changedSlot.Input.IsDefault = false;
            }
        }
    }

    private static readonly Dictionary<Type, System.Numerics.Vector2> _sizesForTypes = new()
                                                                                           {
                                                                                               { typeof(string), new System.Numerics.Vector2(120, 80) },
                                                                                           };
    
    // todo: define this elsewhere so they can be properly hot reloaded
    private static Dictionary<Type, Guid>? _symbolsExtractableFromInputs;
    private static Dictionary<Type, Guid> SymbolsExtractableFromInputs => _symbolsExtractableFromInputs ??=
                                                                               SymbolPackage.AllPackages
                                                                                            .SelectMany(package =>
                                                                                                        {
                                                                                                            return package.Symbols.Values
                                                                                                               .Select(x =>
                                                                                                                {
                                                                                                                    var typeInfo = package
                                                                                                                       .AssemblyInformation
                                                                                                                       .OperatorTypeInfo[x.Id];
                                                                                                                    return (x, typeInfo);
                                                                                                                });
                                                                                                        })
                                                                                            .Where(x => x.typeInfo.ExtractableTypeInfo.IsExtractable)
                                                                                            .ToDictionary(x => x.typeInfo.ExtractableTypeInfo.ExtractableType!,
                                                                                                              x => x.x.Id);

}























