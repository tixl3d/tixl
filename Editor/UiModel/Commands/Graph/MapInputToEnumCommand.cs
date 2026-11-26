#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Declares an enum in an operator's class and maps an int input to it, or with <see cref="IsRemoval"/> maps the input
/// back to a plain value. Each Do/Undo rewrites the operator source and recompiles.
/// </summary>
/// <remarks>
/// A removal deletes the enum's entries from the source without keeping them, so it isn't undoable and clears the
/// undo stack, whose earlier steps may rely on the enum.
/// </remarks>
internal sealed class MapInputToEnumCommand : ICommand
{
    public string Name => IsRemoval ? "Show as Value" : "Map Input to Enum";
    public bool IsUndoable => !IsRemoval;

    public static MapInputToEnumCommand ForNewEnum(Guid symbolId, Guid inputId, string enumName, InputsAndOutputs.EnumEntry[] entries)
    {
        return new MapInputToEnumCommand(symbolId, inputId, enumName, entries);
    }

    public static MapInputToEnumCommand ForRemoval(Guid symbolId, Guid inputId)
    {
        return new MapInputToEnumCommand(symbolId, inputId, null, []);
    }

    public void Do() => LogFailure(TryDo(out var error), error);

    public void Undo()
    {
        if (IsRemoval)
            return;

        LogFailure(TryRemove(out var error), error);
    }

    /** For a first apply whose compile error is shown to the user instead of logged. */
    public bool TryDo([NotNullWhen(false)] out string? error)
    {
        if (IsRemoval)
            return TryRemove(out error);

        if (!TryGetSymbolUi(out var symbolUi, out error))
            return false;

        return InputsAndOutputs.TryAddMappedEnum(symbolUi.Symbol, _inputId, _enumName!, _entries, out error);
    }

    private MapInputToEnumCommand(Guid symbolId, Guid inputId, string? enumName, InputsAndOutputs.EnumEntry[] entries)
    {
        _symbolId = symbolId;
        _inputId = inputId;
        _enumName = enumName;
        _entries = entries;
    }

    private bool IsRemoval => _enumName == null;

    private bool TryRemove([NotNullWhen(false)] out string? error)
    {
        if (!TryGetSymbolUi(out var symbolUi, out error))
            return false;

        return InputsAndOutputs.TryRemoveMappedEnum(symbolUi.Symbol, _inputId, out error);
    }

    private bool TryGetSymbolUi([NotNullWhen(true)] out SymbolUi? symbolUi, [NotNullWhen(false)] out string? error)
    {
        if (SymbolUiRegistry.TryGetSymbolUi(_symbolId, out symbolUi))
        {
            error = null;
            return true;
        }

        error = $"Can't change the input mapping - symbol {_symbolId} is no longer available.";
        return false;
    }

    private static void LogFailure(bool success, string? error)
    {
        if (!success && error != null)
            Log.Warning(error);
    }

    private readonly Guid _symbolId;
    private readonly Guid _inputId;
    private readonly string? _enumName;
    private readonly InputsAndOutputs.EnumEntry[] _entries;
}
