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
    
    public static void ExtractAsConnectedOperator<T>(InputSlot<T> inputSlot, SymbolUi.Child symbolChildUi, Symbol.Child.Input input, Vector2 freePosition,
                                                     MacroCommand? collectInto = null)
    {
        var view = ProjectView.Focused;
        if (view?.InstView == null)
        {
            Log.Warning("Unable to access current view for extractions?");
            return;
        }

        var compositionUi = view.InstView.SymbolUi;
        var compositionInstance = view.InstView.Instance;

        // Find matching symbol
        if (!SymbolsExtractableFromInputs.TryGetValue(input.DefaultValue.ValueType, out var symbolId))
        {
            Log.Warning("Can't extract this parameter type");
            return;
        }

        var ownedMacro = collectInto ?? new MacroCommand("Extract as operator");

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

        ownedMacro.AddAndExecCommand(addSymbolChildCommand);

        var newChildUi = compositionUi.ChildUis[addSymbolChildCommand.AddedChildId];
        var newSymbolChild = newChildUi.SymbolChild;

        // Sadly, we have to apply size manually.
        if (_sizesForTypes.TryGetValue(input.DefaultValue.ValueType, out _))
        {
            newChildUi.Style = SymbolUi.Child.Styles.Resizable;
        }

        var newInstance = compositionInstance.Children[newChildUi.Id];
        var extractableInput = (IExtractedInput<T>)newInstance;
        var outputSlot = extractableInput.OutputSlot;
        extractableInput.SetTypedInputValuesTo(inputSlot.TypedInputValue.Value, out var changedSlots);

        // Record the extracted values so they are reapplied on redo.
        var newValues = new Dictionary<Guid, InputValue>();
        foreach (var changedSlot in changedSlots)
        {
            changedSlot.Input.IsDefault = false;
            newValues[changedSlot.Id] = changedSlot.Input.Value.Clone();
        }
        if (newValues.Count > 0)
        {
            ownedMacro.AddExecutedCommandForUndo(
                new SetExtractedInputValuesCommand(compositionUi.Symbol.Id, newChildUi.Id, newValues));
        }

        // Create connection
        var newConnection = new Symbol.Connection(sourceParentOrChildId: newSymbolChild.Id,
                                                  sourceSlotId: outputSlot.Id,
                                                  targetParentOrChildId: symbolChildUi.SymbolChild.Id,
                                                  targetSlotId: input.Id);
        ownedMacro.AddAndExecCommand(new AddConnectionCommand(compositionUi.Symbol, newConnection, 0));

        if (collectInto == null)
            UndoRedoStack.Add(ownedMacro);
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























