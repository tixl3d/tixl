#nullable enable
namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Snapshots the connection routes of a composition before and after an edit. Create it before changing the
/// routes, then call <see cref="StoreCurrentValues"/> once the edit is complete.
/// </summary>
internal sealed class ModifyConnectionRoutesCommand : ICommand
{
    public string Name { get; internal set; }
    public bool IsUndoable => true;

    internal ModifyConnectionRoutesCommand(Guid compositionSymbolId, ConnectionRoutes currentRoutes, string name = "Edit connection route")
    {
        Name = name;
        _compositionSymbolId = compositionSymbolId;
        _routesBefore = currentRoutes.Clone();
        _routesAfter = _routesBefore;
    }

    internal void StoreCurrentValues()
    {
        if (!TryGetSymbolUi(out var symbolUi))
            return;

        _routesAfter = symbolUi.ConnectionRoutes.Clone();
        symbolUi.FlagAsModified();
    }

    public void Do() => Apply(_routesAfter);

    public void Undo() => Apply(_routesBefore);

    private void Apply(ConnectionRoutes routes)
    {
        if (!TryGetSymbolUi(out var symbolUi))
            return;

        symbolUi.ConnectionRoutes.CopyFrom(routes);
        symbolUi.FlagAsModified();
    }

    private bool TryGetSymbolUi(out SymbolUi symbolUi)
    {
        if (SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out symbolUi!))
            return true;

        Log.Warning($"Can't edit connection routes of missing symbol {_compositionSymbolId}");
        return false;
    }

    private readonly Guid _compositionSymbolId;
    private readonly ConnectionRoutes _routesBefore;
    private ConnectionRoutes _routesAfter;
}
