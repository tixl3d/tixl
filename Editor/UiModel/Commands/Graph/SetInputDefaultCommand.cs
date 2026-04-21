using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Editor.UiModel.Commands.Graph;

internal sealed class SetInputDefaultCommand : ICommand
{
    public string Name => "Set current value as default";
    public bool IsUndoable => true;

    internal SetInputDefaultCommand(Symbol parentComposition, Guid symbolChildId, Symbol.Child.Input input)
    {
        _parentCompositionId = parentComposition.Id;
        _childId = symbolChildId;
        _inputId = input.InputDefinition.Id;

        _previousDefault = input.InputDefinition.DefaultValue.Clone();
        _newDefault = input.Value.Clone();
        _wasDefault = input.IsDefault;
    }

    public void Do() => Apply(_newDefault, isDefaultAfter: true);
    public void Undo() => Apply(_previousDefault, isDefaultAfter: _wasDefault);

    private void Apply(InputValue targetDefault, bool isDefaultAfter)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_parentCompositionId, out var parentSymbolUi))
            return;

        var parentSymbol = parentSymbolUi.Symbol;
        if (!parentSymbol.Children.TryGetValue(_childId, out var symbolChild))
            return;

        if (!symbolChild.Inputs.TryGetValue(_inputId, out var input))
            return;

        var definition = input.InputDefinition;
        if (definition.DefaultValue.IsEditableInputReferenceType)
            definition.DefaultValue.AssignClone(targetDefault);
        else
            definition.DefaultValue.Assign(targetDefault);

        input.IsDefault = isDefaultAfter;

        symbolChild.Symbol.InvalidateInputDefaultInInstances(_inputId);

        if (SymbolUiRegistry.TryGetSymbolUi(symbolChild.Symbol.Id, out var definingSymbolUi))
            definingSymbolUi.FlagAsModified();
    }

    private readonly InputValue _previousDefault;
    private readonly InputValue _newDefault;
    private readonly bool _wasDefault;
    private readonly Guid _parentCompositionId;
    private readonly Guid _childId;
    private readonly Guid _inputId;
}
