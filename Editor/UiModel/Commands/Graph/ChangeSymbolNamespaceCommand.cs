using T3.Core.Operator;
using T3.Core.SystemUi;

namespace T3.Editor.UiModel.Commands.Graph;

internal sealed class ChangeSymbolNamespaceCommand : ICommand
{
    public string Name => "Change Symbol Namespace";
    public bool IsUndoable => true;

    public ChangeSymbolNamespaceCommand(Symbol symbol, EditableSymbolProject targetProject, string newNamespace, ChangeNamespaceAction changeNamespaceAction)
    {
        _newNamespace = newNamespace;
        _symbolId = symbol.Id;
        _originalNamespace = symbol.Namespace;
        _changeNamespaceAction = changeNamespaceAction;
        _originalProject = (EditableSymbolProject)symbol.SymbolPackage;
        _targetProject = targetProject;
    }

    public void Do()
    {
        AssignValue(_newNamespace, _originalProject, _targetProject);
    }

    public void Undo()
    {
        AssignValue(_originalNamespace, _targetProject, _originalProject);
    }

    private void AssignValue(string newNamespace, EditableSymbolProject sourceProject, EditableSymbolProject targetProject)
    {
        var reason = _changeNamespaceAction(_symbolId, newNamespace, sourceProject, targetProject);

        if (!string.IsNullOrWhiteSpace(reason))
            BlockingWindow.Instance.ShowMessageBox(reason, "Could not rename the namespace");
    }

    private readonly Guid _symbolId;
    private readonly string _newNamespace;
    private readonly string _originalNamespace;
    private readonly EditableSymbolProject _originalProject;
    private readonly EditableSymbolProject _targetProject;
    private readonly ChangeNamespaceAction _changeNamespaceAction;
}
    
internal delegate string ChangeNamespaceAction(Guid symbolId, string newNamespace, EditableSymbolProject sourceProject, EditableSymbolProject targetProject);