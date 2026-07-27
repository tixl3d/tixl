#nullable enable
using T3.Core.Logging;
using T3.Core.Output;

namespace T3.Editor.UiModel.Commands.Setup;

internal static class SetupCommands
{
    /// <summary>
    /// Resolves the active setup for a command, guarded by identity: <see cref="T3.Core.Output.Setup.Duplicate"/>
    /// preserves entity GUIDs across venue copies, so a bare Guid lookup would happily apply an undo from one
    /// venue onto another. Commands capture the owning setup's id at construction and no-op when a different
    /// setup is active.
    /// </summary>
    public static bool TryGetSetup(Guid setupId, string commandName, out T3.Core.Output.Setup setup)
    {
        var current = ActiveSetup.Current;
        if (current == null)
        {
            Log.Warning($"{commandName}: no active setup — skipping.");
            setup = null!;
            return false;
        }

        if (current.Id != setupId)
        {
            Log.Warning($"{commandName}: a different setup is active than the one this edit belongs to — skipping.");
            setup = null!;
            return false;
        }

        setup = current;
        return true;
    }
}
