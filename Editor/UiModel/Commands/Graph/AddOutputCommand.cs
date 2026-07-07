#nullable enable
using T3.Editor.UiModel.Modification;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Adds an output slot to an editable operator. Each Do/Undo recompiles the operator source, so this
/// is a heavy command. The output id is fixed once so redo re-creates the same slot.
/// </summary>
internal sealed class AddOutputCommand : ICommand
{
    public string Name => "Add Output";
    public bool IsUndoable => true;

    public AddOutputCommand(Guid symbolId, string outputName, Type outputType, bool isTimeClip, Vector2? posOnCanvas = null)
    {
        _symbolId = symbolId;
        _outputId = Guid.NewGuid();
        _outputName = outputName;
        _outputType = outputType;
        _isTimeClip = isTimeClip;
        _posOnCanvas = posOnCanvas;
    }

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't add output - symbol {_symbolId} is no longer available.");
            return;
        }

        if (!InputsAndOutputs.AddOutputToSymbol(_outputId, _outputName, _isTimeClip, _outputType, symbolUi.Symbol))
            return;

        if (_posOnCanvas == null)
            return;

        // Recompiling recreates the ui entries, so re-resolve before placing the new output
        if (SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var updatedSymbolUi)
            && updatedSymbolUi.OutputUis.TryGetValue(_outputId, out var outputUi))
        {
            outputUi.PosOnCanvas = _posOnCanvas.Value;
            updatedSymbolUi.FlagAsModified();
        }
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't remove output - symbol {_symbolId} is no longer available.");
            return;
        }

        InputsAndOutputs.RemoveInputsAndOutputsFromSymbol([], [_outputId], symbolUi.Symbol);
    }

    private readonly Guid _symbolId;
    private readonly Guid _outputId;
    private readonly string _outputName;
    private readonly Type _outputType;
    private readonly bool _isTimeClip;
    private readonly Vector2? _posOnCanvas;
}
