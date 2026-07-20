#nullable enable
using System.Numerics;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Setup;

/// <summary>
/// Adjusts one surface→output content slice (the source quad in UV space). Same by-value, resolve-by-GUID
/// pattern as <see cref="ChangeOutputMappingQuadCommand"/> — this edits the source region instead of the
/// output-canvas quad.
/// </summary>
internal sealed class ChangeSourceQuadCommand : ICommand
{
    public string Name => "Adjust content slice";
    public bool IsUndoable => true;

    public ChangeSourceQuadCommand(Guid surfaceId, Guid outputId, Vector2[] oldQuad, Vector2[] newQuad)
    {
        _surfaceId = surfaceId;
        _outputId = outputId;
        _oldQuad = (Vector2[])oldQuad.Clone();
        _newQuad = (Vector2[])newQuad.Clone();
    }

    public void Do() => Apply(_newQuad);
    public void Undo() => Apply(_oldQuad);

    private void Apply(Vector2[] quad)
    {
        var surface = ActiveSetup.Current?.Surfaces.Find(s => s.Id == _surfaceId);
        var mapping = surface?.OutputMappings.Find(m => m.OutputId == _outputId);
        if (mapping == null || mapping.SourceQuad.Length < 4)
        {
            Log.Warning($"Content-slice target (surface {_surfaceId} / output {_outputId}) no longer exists — skipping.");
            return;
        }

        Array.Copy(quad, mapping.SourceQuad, 4);
        OutputSetupHandling.SaveActive();
    }

    private readonly Guid _surfaceId;
    private readonly Guid _outputId;
    private readonly Vector2[] _oldQuad;
    private readonly Vector2[] _newQuad;
}
