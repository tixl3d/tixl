#nullable enable
using System.Numerics;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Setup;

/// <summary>
/// Adjusts one surface→output corner-pin quad. Setup entities are plain data (not Symbol
/// instances), so the command resolves them by GUID against the active setup rather than
/// caching references, and persists the setup after each apply. The quads are captured by
/// value, so undo/redo is independent of later graph or reload state.
/// </summary>
internal sealed class ChangeOutputMappingQuadCommand : ICommand
{
    public string Name => "Adjust corner pin";
    public bool IsUndoable => true;

    public ChangeOutputMappingQuadCommand(Guid surfaceId, Guid outputId, Vector2[] oldQuad, Vector2[] newQuad)
    {
        _setupId = ActiveSetup.Current?.Id ?? Guid.Empty;
        _surfaceId = surfaceId;
        _outputId = outputId;
        _oldQuad = (Vector2[])oldQuad.Clone();
        _newQuad = (Vector2[])newQuad.Clone();
    }

    public void Do() => Apply(_newQuad);
    public void Undo() => Apply(_oldQuad);

    private void Apply(Vector2[] quad)
    {
        if (!SetupCommands.TryGetSetup(_setupId, Name, out var setup))
            return;

        var surface = setup.FindSurface(_surfaceId);
        var mapping = surface?.OutputMappings.Find(m => m.OutputId == _outputId);
        if (mapping == null || mapping.Quad.Length < 4)
        {
            Log.Warning($"Corner-pin target (surface {_surfaceId} / output {_outputId}) no longer exists — skipping.");
            return;
        }

        Array.Copy(quad, mapping.Quad, 4);
        OutputSetupHandling.SaveActive();
    }

    private readonly Guid _setupId;
    private readonly Guid _surfaceId;
    private readonly Guid _outputId;
    private readonly Vector2[] _oldQuad;
    private readonly Vector2[] _newQuad;
}
