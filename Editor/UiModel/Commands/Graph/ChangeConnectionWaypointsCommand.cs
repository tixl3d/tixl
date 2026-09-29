using T3.Core.DataTypes.Vector;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Replaces the manual bend points that reroute a single connection. An empty list restores the
/// connection's automatic route.
/// </summary>
internal sealed class ChangeConnectionWaypointsCommand : ICommand
{
    public string Name => "Reroute Connection";
    public bool IsUndoable => true;

    internal ChangeConnectionWaypointsCommand(Guid parentSymbolId,
                                              Guid ownerChildId,
                                              SymbolUi.Child.ConnectionTarget connectionTarget,
                                              List<Vector2>? beforeWaypoints,
                                              List<Vector2> afterWaypoints)
    {
        _parentSymbolId = parentSymbolId;
        _ownerChildId = ownerChildId;
        _connectionTarget = connectionTarget;
        _beforeWaypoints = beforeWaypoints == null ? null : [..beforeWaypoints];
        _afterWaypoints = [..afterWaypoints];
    }

    public void Do() => Assign(_afterWaypoints);

    public void Undo()
    {
        Assign(_beforeWaypoints);
    }

    private void Assign(List<Vector2>? waypoints)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_parentSymbolId, out var symbolUi))
            return;

        if (!symbolUi.ChildUis.TryGetValue(_ownerChildId, out var childUi))
            return;

        if (waypoints == null)
        {
            childUi.RemoveConnectionWaypoints(_connectionTarget);
        }
        else
        {
            childUi.SetConnectionWaypoints(_connectionTarget, [..waypoints]);
        }

        // The saved .t3ui is what makes a reroute outlive the session.
        symbolUi.FlagAsModified();
    }

    private readonly Guid _parentSymbolId;
    private readonly Guid _ownerChildId;
    private readonly SymbolUi.Child.ConnectionTarget _connectionTarget;
    private readonly List<Vector2>? _beforeWaypoints;
    private readonly List<Vector2> _afterWaypoints;
}
