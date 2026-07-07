#nullable enable
using T3.Editor.UiModel.Modification;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Adds an input slot to an editable operator. Each Do/Undo recompiles the operator source, so this
/// is a heavy command. The input id is fixed once so redo re-creates the same slot.
/// </summary>
internal sealed class AddInputCommand : ICommand
{
    public string Name => "Add Input";
    public bool IsUndoable => true;

    public AddInputCommand(Guid symbolId, string inputName, Type inputType, bool multiInput, Vector2? posOnCanvas = null)
    {
        _symbolId = symbolId;
        _inputId = Guid.NewGuid();
        _inputName = inputName;
        _inputType = inputType;
        _multiInput = multiInput;
        _posOnCanvas = posOnCanvas;
    }

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't add input - symbol {_symbolId} is no longer available.");
            return;
        }

        if (!InputsAndOutputs.AddInputToSymbol(_inputId, _inputName, _multiInput, _inputType, symbolUi.Symbol))
            return;

        if (_posOnCanvas == null)
            return;

        // Recompiling recreates the ui entries, so re-resolve before placing the new input
        if (SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var updatedSymbolUi)
            && updatedSymbolUi.InputUis.TryGetValue(_inputId, out var inputUi))
        {
            inputUi.PosOnCanvas = _posOnCanvas.Value;
            updatedSymbolUi.FlagAsModified();
        }
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't remove input - symbol {_symbolId} is no longer available.");
            return;
        }

        InputsAndOutputs.RemoveInputsAndOutputsFromSymbol([_inputId], [], symbolUi.Symbol);
    }

    private readonly Guid _symbolId;
    private readonly Guid _inputId;
    private readonly string _inputName;
    private readonly Type _inputType;
    private readonly bool _multiInput;
    private readonly Vector2? _posOnCanvas;
}
