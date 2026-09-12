#nullable enable
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using GraphUtils = T3.Editor.UiModel.Helpers.GraphUtils;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Adds a new input to the composition and connects it to a child's parameter. The parameter's current
/// value becomes the new input's default and its input settings (relevancy, range, scale, format...) are carried over.
/// </summary>
/// <remarks>
/// Adding the input recompiles the composition synchronously. The <see cref="Symbol"/> survives, but instances
/// and ui entries are recreated, so everything after the recompile works on re-resolved data.
/// </remarks>
internal sealed class PublishAsInputCommand : ICommand
{
    public string Name => "Publish as Input";
    public bool IsUndoable => true;

    public PublishAsInputCommand(Symbol compositionSymbol, Guid childId, Symbol.Child.Input input, IInputUi sourceInputUi, Vector2 posOnCanvas)
    {
        _compositionSymbolId = compositionSymbol.Id;
        _childId = childId;
        _targetInputId = input.Id;
        _newInputId = Guid.NewGuid();
        _inputName = FindUniqueInputName(input.Name, compositionSymbol);
        _inputType = input.DefaultValue.ValueType;
        _value = input.Value.Clone();
        _inputUiSettings = SerializeInputUiSettings(sourceInputUi);
        _posOnCanvas = posOnCanvas;
    }

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionUi))
        {
            Log.Warning($"Can't publish input - symbol {_compositionSymbolId} is no longer available.");
            return;
        }

        if (!InputsAndOutputs.AddInputToSymbol(_newInputId, _inputName, false, _inputType, compositionUi.Symbol))
            return;

        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out compositionUi))
            return;

        var symbol = compositionUi.Symbol;
        var definition = symbol.InputDefinitions.FirstOrDefault(d => d.Id == _newInputId);
        if (definition == null)
        {
            Log.Warning($"Can't find published input {_inputName} after recompiling [{symbol.Name}].");
            return;
        }

        if (definition.DefaultValue.IsEditableInputReferenceType)
            definition.DefaultValue.AssignClone(_value);
        else
            definition.DefaultValue.Assign(_value);

        symbol.InvalidateInputDefaultInInstances(_newInputId);

        if (compositionUi.InputUis.TryGetValue(_newInputId, out var inputUi))
        {
            inputUi.Read(_inputUiSettings);
            inputUi.PosOnCanvas = _posOnCanvas;
        }

        if (symbol.Children.ContainsKey(_childId))
        {
            var connection = new Symbol.Connection(sourceParentOrChildId: Guid.Empty,
                                                   sourceSlotId: _newInputId,
                                                   targetParentOrChildId: _childId,
                                                   targetSlotId: _targetInputId);
            if (!symbol.Connections.Contains(connection))
                symbol.AddConnection(connection, 0);
        }

        compositionUi.FlagAsModified();
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionUi))
        {
            Log.Warning($"Can't remove published input - symbol {_compositionSymbolId} is no longer available.");
            return;
        }

        // Recompiling without the input also prunes its connection to the child.
        InputsAndOutputs.RemoveInputsAndOutputsFromSymbol([_newInputId], [], compositionUi.Symbol);
    }

    private static string FindUniqueInputName(string baseName, Symbol symbol)
    {
        var name = baseName;
        for (var suffix = 2; name == symbol.Name || !GraphUtils.IsNewFieldNameValid(name, symbol, out _); suffix++)
        {
            name = baseName + suffix;
        }

        return name;
    }

    /// <summary>
    /// Uses the persistence format because it covers every setting, unlike <see cref="IInputUi.Clone"/>.
    /// </summary>
    private static JToken SerializeInputUiSettings(IInputUi inputUi)
    {
        using var stringWriter = new StringWriter();
        using (var writer = new JsonTextWriter(stringWriter))
        {
            writer.WriteStartObject();
            inputUi.Write(writer);
            writer.WriteEndObject();
        }

        return JToken.Parse(stringWriter.ToString());
    }

    private readonly Guid _compositionSymbolId;
    private readonly Guid _childId;
    private readonly Guid _targetInputId;
    private readonly Guid _newInputId;
    private readonly string _inputName;
    private readonly Type _inputType;
    private readonly InputValue _value;
    private readonly JToken _inputUiSettings;
    private readonly Vector2 _posOnCanvas;
}
