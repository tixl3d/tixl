#nullable enable
using T3.Core.Output;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Undo for setup edits: whole-state snapshots. Setup files are a few KB of plain DTOs, so a snapshot before
/// and after is simpler and more robust than per-operation inverses — every cascade an edit performs is
/// captured by construction. Each step also saves the setup, so the file on disk never lags the undo stack.
/// </summary>
internal static class SetupUndo
{
    /// <summary>Wraps a structural mutation in one undo step; nothing is pushed or saved when the setup came back unchanged.</summary>
    public static void RunUndoable(string name, Setup setup, Action mutate)
    {
        var oldJson = setup.ToJsonString();
        mutate();
        Commit(setup, name, oldJson);
    }

    /// <summary>
    /// Closes a continuous gesture (a drag, a field edit) as one undo step: <paramref name="oldJson"/> is the
    /// setup's snapshot from the gesture's start; nothing is pushed when the setup came back unchanged.
    /// </summary>
    public static void CommitGesture(Setup setup, string name, string oldJson)
    {
        Commit(setup, name, oldJson);
    }

    private static void Commit(Setup setup, string name, string oldJson)
    {
        var newJson = setup.ToJsonString();
        if (newJson == oldJson)
            return;

        // Already applied by the caller, so Add rather than AddAndExecute.
        UndoRedoStack.Add(new SetupSnapshotCommand(name, setup.Id, oldJson, newJson));
        OutputSetupHandling.SaveActive();
    }
}
