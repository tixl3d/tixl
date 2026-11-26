#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Replaces the entries of an enum declared inside an operator's class. Each Do/Undo rewrites the operator
/// source and recompiles.
/// </summary>
internal sealed class ChangeEnumEntriesCommand : ICommand
{
    public string Name => "Edit Enum Entries";
    public bool IsUndoable => true;

    public ChangeEnumEntriesCommand(Guid symbolId, string enumName, InputsAndOutputs.EnumEntry[] oldEntries, InputsAndOutputs.EnumEntry[] newEntries)
    {
        _symbolId = symbolId;
        _enumName = enumName;
        _oldEntries = oldEntries;
        _newEntries = newEntries;
    }

    public void Do() => ApplyAndLogFailure(_newEntries);
    public void Undo() => ApplyAndLogFailure(_oldEntries);

    /** For a first apply whose compile error is shown to the user instead of logged. */
    public bool TryDo([NotNullWhen(false)] out string? error) => TryApply(_newEntries, out error);

    private void ApplyAndLogFailure(InputsAndOutputs.EnumEntry[] entries)
    {
        if (!TryApply(entries, out var error))
            Log.Warning(error);
    }

    private bool TryApply(InputsAndOutputs.EnumEntry[] entries, [NotNullWhen(false)] out string? error)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            error = $"Can't edit enum {_enumName} - symbol {_symbolId} is no longer available.";
            return false;
        }

        return InputsAndOutputs.TryReplaceEnumEntries(symbolUi.Symbol, _enumName, entries, out error);
    }

    private readonly Guid _symbolId;
    private readonly string _enumName;
    private readonly InputsAndOutputs.EnumEntry[] _oldEntries;
    private readonly InputsAndOutputs.EnumEntry[] _newEntries;
}
