#nullable enable
using T3.SystemUi;

namespace T3.Editor.UiModel.Commands;

/// <summary>
/// Wraps an edit that an operator applied to its own data, pushed through <see cref="OperatorUndoRedo"/>.
/// The operator's callbacks resolve their target themselves, so the command holds no instance references.
/// </summary>
internal sealed class OperatorEditCommand : ICommand
{
    private OperatorEditCommand(string name, Action undo, Action redo)
    {
        Name = name;
        _undo = undo;
        _redo = redo;
    }

    public string Name { get; }
    public bool IsUndoable => true;

    public void Undo() => _undo();
    public void Do() => _redo();

    internal static void RegisterAsOperatorUndoHandler()
    {
        OperatorUndoRedo.AddCommand = static (name, undo, redo) => UndoRedoStack.Add(new OperatorEditCommand(name, undo, redo));
    }

    private readonly Action _undo;
    private readonly Action _redo;
}
