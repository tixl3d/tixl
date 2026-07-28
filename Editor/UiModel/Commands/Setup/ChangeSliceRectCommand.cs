#nullable enable
using System.Numerics;
using T3.Core.Logging;

namespace T3.Editor.UiModel.Commands.Setup;

/// <summary>
/// Changes a slice's UV rect. Setup entities are plain data, so the slice is resolved by GUID against
/// the active setup (guarded by setup identity) and the rects are captured by value.
/// </summary>
internal sealed class ChangeSliceRectCommand : ICommand
{
    public string Name => "Adjust slice";
    public bool IsUndoable => true;

    public ChangeSliceRectCommand(Guid sliceId, Vector4 oldRect, Vector4 newRect)
    {
        _setupId = T3.Core.Output.ActiveSetup.Current?.Id ?? Guid.Empty;
        _sliceId = sliceId;
        _oldRect = oldRect;
        _newRect = newRect;
    }

    public void Do() => Apply(_newRect);
    public void Undo() => Apply(_oldRect);

    private void Apply(Vector4 rect)
    {
        if (!SetupCommands.TryGetSetup(_setupId, Name, out var setup))
            return;

        var slice = setup.FindSlice(_sliceId);
        if (slice == null)
        {
            Log.Warning($"Slice {_sliceId} no longer exists — skipping.");
            return;
        }

        slice.UvRect = rect;
        T3.Editor.UiModel.ProjectHandling.OutputSetupHandling.SaveActive();
    }

    private readonly Guid _setupId;
    private readonly Guid _sliceId;
    private readonly Vector4 _oldRect;
    private readonly Vector4 _newRect;
}
