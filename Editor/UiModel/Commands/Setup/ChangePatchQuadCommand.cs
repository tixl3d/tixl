#nullable enable
using System.Numerics;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Setup;

/// <summary>
/// Adjusts one patch's canvas quad (a corner drag, an edge crop or a whole move on the output canvas).
/// Resolved by GUID against the active setup and captured by value, like the corner-pin command.
/// </summary>
internal sealed class ChangePatchQuadCommand : ICommand
{
    public string Name => "Adjust patch";
    public bool IsUndoable => true;

    public ChangePatchQuadCommand(Guid patchId, Vector2[] oldQuad, Vector2[] newQuad)
    {
        _setupId = ActiveSetup.Current?.Id ?? Guid.Empty;
        _patchId = patchId;
        _oldQuad = (Vector2[])oldQuad.Clone();
        _newQuad = (Vector2[])newQuad.Clone();
    }

    public void Do() => Apply(_newQuad);
    public void Undo() => Apply(_oldQuad);

    private void Apply(Vector2[] quad)
    {
        if (!SetupCommands.TryGetSetup(_setupId, Name, out var setup))
            return;

        var patch = setup.FindPatch(_patchId, out _);
        if (patch == null || patch.Quad.Length < 4)
        {
            Log.Warning($"Patch {_patchId} no longer exists — skipping.");
            return;
        }

        Array.Copy(quad, patch.Quad, 4);
        OutputSetupHandling.SaveActive();
    }

    private readonly Guid _setupId;
    private readonly Guid _patchId;
    private readonly Vector2[] _oldQuad;
    private readonly Vector2[] _newQuad;
}
