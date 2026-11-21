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
        var outer = _openMacro;
        var macro = new MacroCommand(name);
        _openMacro = macro;
        try
        {
            mutate();
        }
        finally
        {
            _openMacro = outer;
        }

        Commit(setup, name, oldJson, macro);
    }

    /// <summary>
    /// Executes a command that changes the graph as part of a setup edit (a laser camera losing the output it
    /// was routed to). Inside <see cref="RunUndoable"/> it joins that step, so one undo reverts both; outside
    /// it is a step of its own.
    /// </summary>
    internal static void ExecuteCompanion(ICommand command)
    {
        if (_openMacro == null)
        {
            UndoRedoStack.AddAndExecute(command);
            return;
        }

        _openMacro.AddAndExecCommand(command);
    }

    /// <summary>The step being built by the innermost <see cref="RunUndoable"/>; the UI thread is the only caller.</summary>
    private static MacroCommand? _openMacro;

    /// <summary>
    /// Closes a continuous gesture (a drag, a field edit) as one undo step: <paramref name="oldJson"/> is the
    /// setup's snapshot from the gesture's start; nothing is pushed when the setup came back unchanged.
    /// </summary>
    public static void CommitGesture(Setup setup, string name, string oldJson)
    {
        Commit(setup, name, oldJson);
    }

    private static void Commit(Setup setup, string name, string oldJson, MacroCommand? companions = null)
    {
        var newJson = setup.ToJsonString();
        var setupChanged = newJson != oldJson;
        var hasCompanions = companions is { Count: > 0 };
        if (!setupChanged && !hasCompanions)
            return;

        // Already applied by the caller, so Add rather than AddAndExecute.
        if (!hasCompanions)
        {
            UndoRedoStack.Add(new SetupSnapshotCommand(name, setup.Id, oldJson, newJson));
        }
        else
        {
            if (setupChanged)
                companions!.AddExecutedCommandForUndo(new SetupSnapshotCommand(name, setup.Id, oldJson, newJson));

            UndoRedoStack.Add(companions!);
        }

        OutputSetupHandling.SaveActive();
    }
}
