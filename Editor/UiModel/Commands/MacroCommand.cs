namespace T3.Editor.UiModel.Commands;

internal sealed class MacroCommand : ICommand
{
    internal MacroCommand(string name, IEnumerable<ICommand> commands)
    {
        Name = name;
        _commands = commands.ToList();
    }
        
    internal MacroCommand(string name)
    {
        Name = name;
        _commands = new List<ICommand>();
    }

    public string Name { get; set; }
    public bool IsUndoable => _commands.Aggregate(true, (result, current) => result && current.IsUndoable);

    /// <summary>
    /// Adds an already executed command to the marco-command so it can be undone
    /// </summary>
    internal void AddExecutedCommandForUndo(ICommand command)
    {
        _commands.Add(command);
    }
        
    /// <summary>
    /// For certain macro-operations it can be necessary to executed some of its sub commands early on. 
    /// </summary>
    internal void AddAndExecCommand(ICommand command)
    {
        _commands.Add(command);
        command.Do();
    }
        
    public void Do()
    {
        _commands.ForEach(c => c.Do());
    }

    public void Undo()
    {
        var tmpCommands = new List<ICommand>(_commands);
        tmpCommands.Reverse();
        tmpCommands.ForEach(c => c.Undo());
    }

    internal int Count => _commands.Count;
    private readonly List<ICommand> _commands;
    public override string ToString()
    {
        return Name;
    }
}