#nullable enable
using System.Numerics;
using T3.Core.Logging;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Setup;

/// <summary>
/// Moves a measuring line's endpoints. Annotations live in their surface's list and have no id of their
/// own, so the command addresses one by surface GUID + index — defensive against the list having shrunk
/// (an add/remove between the drag and the undo).
/// </summary>
internal sealed class ChangeAnnotationCommand : ICommand
{
    public string Name => "Move measuring line";
    public bool IsUndoable => true;

    public ChangeAnnotationCommand(Guid surfaceId, int index, Vector2 oldP1, Vector2 oldP2, Vector2 newP1, Vector2 newP2)
    {
        _setupId = T3.Core.Output.ActiveSetup.Current?.Id ?? Guid.Empty;
        _surfaceId = surfaceId;
        _index = index;
        _oldP1 = oldP1;
        _oldP2 = oldP2;
        _newP1 = newP1;
        _newP2 = newP2;
    }

    public void Do() => Apply(_newP1, _newP2);
    public void Undo() => Apply(_oldP1, _oldP2);

    private void Apply(Vector2 p1, Vector2 p2)
    {
        if (!SetupCommands.TryGetSetup(_setupId, Name, out var setup))
            return;

        var surface = setup.FindSurface(_surfaceId);
        if (surface == null || _index < 0 || _index >= surface.Annotations.Count)
        {
            Log.Warning($"Measuring line {_index} on surface {_surfaceId} no longer exists — skipping.");
            return;
        }

        surface.Annotations[_index].P1 = p1;
        surface.Annotations[_index].P2 = p2;
        OutputSetupHandling.SaveActive();
    }

    private readonly Guid _setupId;
    private readonly Guid _surfaceId;
    private readonly int _index;
    private readonly Vector2 _oldP1;
    private readonly Vector2 _oldP2;
    private readonly Vector2 _newP1;
    private readonly Vector2 _newP2;
}
