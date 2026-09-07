#nullable enable
using System.Text;
using T3.Editor.Gui;

namespace T3.Editor.UiModel.Commands;

public interface ICommand
{
    string Name { get; }
    bool IsUndoable { get; }
    void Undo();
    void Do();
}

internal static class CommandExtensions
{
    public static void LogError(this ICommand command, bool isUndo, string message, bool logStackTrace = true)
    {
        Log.Warning($"{command.GetType().Name} {(isUndo ? "Undo" : "Execute")}: {message}");
        if (logStackTrace)
        {
            Log.Debug(Environment.StackTrace);
        }
    }
}



public static class UndoRedoStack
{
    internal static bool CanUndo => UndoStack.Count > 0;
    internal static bool CanRedo => RedoStack.Count > 0;

    internal static void AddAndExecute(ICommand command)
    {
        Add(command);

        command.Do();
    }

    public static void Add(ICommand command)
    {
        if (command.IsUndoable)
        {
            UndoStack.Push(command);
            RedoStack.Clear();
        }
        else
        {
            Clear();
        }
    }

    internal static string? GetNextUndoTitle()
    {
        return !CanUndo ? null : UndoStack.Peek().Name;
    }

    internal static void Undo()
    {
        if (CanUndo)
        {
            var command = UndoStack.Pop();
            command.Undo();
            RedoStack.Push(command);
            FrameStats.Current.UndoRedoTriggered = true;
        }

    }

    internal static void Redo()
    {
        if (CanRedo)
        {
            var command = RedoStack.Pop();
            command.Do();
            UndoStack.Push(command);
            FrameStats.Current.UndoRedoTriggered = true;
        }
    }

    internal static void Clear()
    {
        UndoStack.Clear();
        RedoStack.Clear();
    }

    internal static string GetUndoStackAsString()
    {
        var sb = new StringBuilder();
        var index = 0;
        foreach (var a in UndoStack)
        {
            sb.Append(a.Name);
            sb.Append('\n');
            index++;
            if (index > 20)
                break;
        }

        return sb.ToString();
    }

    internal static Stack<ICommand> UndoStack { get; } = new();
    private static Stack<ICommand> RedoStack { get; } = new();
}