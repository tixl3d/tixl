#nullable enable
namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>Sets or clears the smart title template of a symbol, which changes the label of all its instances.</summary>
internal sealed class ChangeSmartTitleCommand : ICommand
{
    public string Name => "Change smart title";
    public bool IsUndoable => true;

    /// <remarks><see cref="SmartTitle"/> is immutable, so holding the instances is safe across reloads.</remarks>
    internal ChangeSmartTitleCommand(Guid symbolId, SmartTitle? previousTitle, SmartTitle? newTitle)
    {
        _symbolId = symbolId;
        _previousTitle = previousTitle;
        _newTitle = newTitle;
    }

    public void Do() => Apply(_newTitle);
    public void Undo() => Apply(_previousTitle);

    private void Apply(SmartTitle? title)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't change smart title: symbol {_symbolId} is no longer loaded.");
            return;
        }

        symbolUi.SmartTitle = title;
        symbolUi.FlagAsModified();
    }

    private readonly Guid _symbolId;
    private readonly SmartTitle? _previousTitle;
    private readonly SmartTitle? _newTitle;
}
