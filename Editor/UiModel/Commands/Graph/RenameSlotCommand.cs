#nullable enable
using T3.Editor.UiModel.Modification;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Renames an input or output slot of an editable operator. Each Do/Undo rewrites the
/// operator source and recompiles, so this is a heavy command. The slot's Guid is left
/// untouched by a rename, so connections and animations survive both directions.
/// </summary>
internal sealed class RenameSlotCommand : ICommand
{
    public string Name => _isInput ? "Rename Input" : "Rename Output";
    public bool IsUndoable => true;

    public RenameSlotCommand(Guid symbolId, Guid slotId, string oldName, string newName, bool isInput)
    {
        _symbolId = symbolId;
        _slotId = slotId;
        _oldName = oldName;
        _newName = newName;
        _isInput = isInput;
    }

    public void Do() => Rename(_newName);
    public void Undo() => Rename(_oldName);

    private void Rename(string targetName)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't rename slot - symbol {_symbolId} is no longer available.");
            return;
        }

        var success = _isInput
                          ? InputsAndOutputs.RenameInput(symbolUi.Symbol, _slotId, targetName, dryRun: false, out var warning)
                          : InputsAndOutputs.RenameOutput(symbolUi.Symbol, _slotId, targetName, dryRun: false, out warning);

        if (!success && !string.IsNullOrEmpty(warning))
            Log.Warning(warning);
    }

    private readonly Guid _symbolId;
    private readonly Guid _slotId;
    private readonly string _oldName;
    private readonly string _newName;
    private readonly bool _isInput;
}
