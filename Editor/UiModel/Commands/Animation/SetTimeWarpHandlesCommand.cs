#nullable enable
using T3.Editor.Gui.Windows.TimeLine;

namespace T3.Editor.UiModel.Commands.Animation;

/// <summary>
/// Snapshots and restores the TimeWarp-handle positions held by a <see cref="TimeWarpHandleSet"/>
/// so undo/redo of a drag interaction that moves handles also restores their locations.
/// Creating or toggling handles is not captured (by design).
/// </summary>
internal sealed class SetTimeWarpHandlesCommand : ICommand
{
    public string Name => "TimeWarp handles";
    public bool IsUndoable => true;

    private readonly TimeWarpHandleSet _handles;
    private readonly double[] _oldHandles;
    private double[] _newHandles;

    internal SetTimeWarpHandlesCommand(TimeWarpHandleSet handles, IReadOnlyList<double> initialHandles)
    {
        _handles = handles;
        _oldHandles = new double[initialHandles.Count];
        for (var i = 0; i < initialHandles.Count; i++)
            _oldHandles[i] = initialHandles[i];
        _newHandles = _oldHandles;
    }

    internal void StoreCurrentValues(TimeWarpHandleSet handles)
    {
        _newHandles = new double[handles.Count];
        for (var i = 0; i < handles.Count; i++)
            _newHandles[i] = handles[i];
    }

    public void Undo() => _handles.RestoreFromSnapshot(_oldHandles);
    public void Do() => _handles.RestoreFromSnapshot(_newHandles);
}
