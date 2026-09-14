#nullable enable
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Setup;

/// <summary>
/// Undo for setup edits as whole-setup JSON snapshots — the one undo mechanism for setup data.
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
        if (!TryGetSetup(out var setup))
            return;

        T3.Core.Output.Setup restored;
        try
        {
            restored = T3.Core.Output.Setup.ReadFromJson(JObject.Parse(json));
        }
        catch (Exception e)
        {
            Log.Warning($"{Name}: can't restore setup snapshot - {e.Message}");
            return;
        }

        setup.Name = restored.Name;
        setup.ReferenceImages = restored.ReferenceImages;
        setup.Surfaces = restored.Surfaces;
        setup.ContentSources = restored.ContentSources;
        setup.Slices = restored.Slices;
        setup.Outputs = restored.Outputs;
        setup.Props = restored.Props;
        setup.FloorPlans = restored.FloorPlans;
        OutputSetupHandling.SaveActive();
    }

    /// <summary>
    /// Resolves the active setup guarded by identity: <see cref="T3.Core.Output.Setup.Duplicate"/> preserves
    /// entity GUIDs across venue copies, so a bare lookup would happily apply an undo from one venue onto
    /// another. The command no-ops when a different setup is active than the one it was recorded on.
    /// </summary>
    private bool TryGetSetup([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T3.Core.Output.Setup? setup)
    {
        setup = ActiveSetup.Current;
        if (setup == null)
        {
            Log.Warning($"{Name}: no active setup — skipping.");
            return false;
        }

        if (setup.Id != _setupId)
        {
            Log.Warning($"{Name}: a different setup is active than the one this edit belongs to — skipping.");
            setup = null;
            return false;
        }

        return true;
    }

    private readonly Guid _setupId;
    private readonly string _oldJson;
    private readonly string _newJson;
}
