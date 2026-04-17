#nullable enable
using T3.Editor.Gui.Windows.TimeLine;

namespace T3.Editor.UiModel.Commands.Animation;

/// <summary>
/// Snapshots and restores the <see cref="SelectionRangeIndicator"/>'s TimeWarp-handle positions
/// so that undo/redo of a drag interaction that moves handles also restores their locations.
/// Creating or toggling handles is not captured (per design).
/// </summary>
internal sealed class SetTimeWarpHandlesCommand : ICommand
{
    public string Name => "TimeWarp handles";
    public bool IsUndoable => true;

    private readonly SelectionRangeIndicator _sri;
    private readonly double[] _oldHandles;
    private double[] _newHandles;

    internal SetTimeWarpHandlesCommand(SelectionRangeIndicator sri, IReadOnlyList<double> initialHandles)
    {
        _sri = sri;
        _oldHandles = new double[initialHandles.Count];
        for (var i = 0; i < initialHandles.Count; i++)
            _oldHandles[i] = initialHandles[i];
        _newHandles = _oldHandles;
    }

    internal void StoreCurrentValues(IReadOnlyList<double> handles)
    {
        _newHandles = new double[handles.Count];
        for (var i = 0; i < handles.Count; i++)
            _newHandles[i] = handles[i];
    }

    public void Undo() => _sri.RestoreTimeWarpHandlesForUndo(_oldHandles);
    public void Do() => _sri.RestoreTimeWarpHandlesForUndo(_newHandles);
}
