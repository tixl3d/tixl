namespace T3.Editor.UiModel.Commands.Sections;

public sealed class ChangeSectionTextCommand : ICommand
{
    public string Name => "Change Section text";
    public bool IsUndoable => true;

    public ChangeSectionTextCommand(Section section, string newText)
    {
        _section = section;
        _originalText = section.Title;
        NewText = newText;
    }


    public void Do()
    {
        _section.Title = NewText;
    }
        
    public void Undo()
    {
        _section.Title = _originalText;
    }
        
        
    private readonly Section _section;
    private readonly string _originalText;
    public  string NewText { get; set; }
}