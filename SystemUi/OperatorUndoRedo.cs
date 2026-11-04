namespace T3.SystemUi;

/// <summary>
/// Lets operators put edits they make to their own data (e.g. sketch strokes) onto the editor's undo stack.
/// The editor sets <see cref="AddCommand"/> at startup; elsewhere (e.g. in the Player) adding is a no-op.
/// </summary>
public static class OperatorUndoRedo
{
    /// <summary>
    /// Registers an edit that the operator has already applied. Call only for explicit user actions,
    /// since every call allocates a command and clears the redo stack.
    /// </summary>
    public static void Add(string name, Action undo, Action redo) => AddCommand?.Invoke(name, undo, redo);

    public static Action<string, Action, Action>? AddCommand { get; set; }
}
