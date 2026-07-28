#nullable enable
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Setup;

/// <summary>
/// Undo for structural setup edits (add/delete/duplicate/rebind/rename) as whole-setup JSON snapshots.
/// A setup file is a few KB of plain DTOs, so restoring the complete state is simpler and more robust
/// than per-operation inverse logic — every cascade an edit performs is captured by construction.
/// Restores IN PLACE (the live <see cref="T3.Core.Output.Setup"/> instance keeps its identity), so every
/// holder of the setup reference stays valid; selection prunes itself against the restored entity lists.
/// <para>Note: the per-machine display bindings live outside the setup file, so undoing an output
/// deletion restores the output but not its display binding.</para>
/// </summary>
internal sealed class SetupSnapshotCommand : ICommand
{
    public string Name { get; }
    public bool IsUndoable => true;

    /// <summary>Construct AFTER the mutation was applied, with the pre-mutation JSON.</summary>
    public SetupSnapshotCommand(string name, Guid setupId, string oldJson, string newJson)
    {
        Name = name;
        _setupId = setupId;
        _oldJson = oldJson;
        _newJson = newJson;
    }

    public void Do() => Apply(_newJson);
    public void Undo() => Apply(_oldJson);

    private void Apply(string json)
    {
        if (!SetupCommands.TryGetSetup(_setupId, Name, out var setup))
            return;

        T3.Core.Output.Setup? restored;
        try
        {
            restored = T3.Core.Output.Setup.ReadFromJson(JObject.Parse(json));
        }
        catch (Exception e)
        {
            Log.Warning($"{Name}: can't restore setup snapshot - {e.Message}");
            return;
        }

        if (restored == null)
            return;

        setup.Name = restored.Name;
        setup.ReferenceImages = restored.ReferenceImages;
        setup.Surfaces = restored.Surfaces;
        setup.ContentSources = restored.ContentSources;
        setup.Slices = restored.Slices;
        setup.Outputs = restored.Outputs;
        setup.Props = restored.Props;
        OutputSetupHandling.SaveActive();
    }

    private readonly Guid _setupId;
    private readonly string _oldJson;
    private readonly string _newJson;
}
