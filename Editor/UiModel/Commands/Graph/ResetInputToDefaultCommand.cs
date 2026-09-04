using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Editor.UiModel.Commands.Graph;

internal sealed class ResetInputToDefault : ICommand
{
    public string Name => "Reset Input Value to default";
    public bool IsUndoable => true;

    private readonly string _creationStack;

    internal ResetInputToDefault(Symbol parent, Guid symbolChildId, Symbol.Child.Input input)
    {
        _inputParentSymbolId = parent.Id;

        if (!SymbolUiRegistry.TryGetSymbolUi(_inputParentSymbolId, out _))
        {
            throw new InvalidOperationException("Symbol not found");
        }
            
        _childId = symbolChildId;
        _inputId = input.InputDefinition.Id;

        OriginalValue = input.Value.Clone();
        _wasDefault = input.IsDefault;
        _creationStack = Environment.StackTrace;
    }

    public void Undo()
    {
        try
        {
            AssignValue(_wasDefault);
        }
        catch (Exception e)
        {
            this.LogError(true, $"Failed! Command created at:\n{_creationStack}\n\n{e}\n\n", false);
        }
    }

    public void Do()
    {
        try
        {
            AssignValue(true);
        }
        catch (Exception e)
        {
            this.LogError(false, $"Failed! Command created at:\n{_creationStack}\n\n{e}\n\n", false);
        }
    }

    private void AssignValue(bool shouldBeDefault)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_inputParentSymbolId, out var parentSymbolUi))
        {
            throw new InvalidOperationException("Symbol not found");
        }
            
        var parentSymbol = parentSymbolUi!.Symbol;
        var symbolChild = parentSymbol.Children[_childId];
        var input = symbolChild.Inputs[_inputId];

        if (shouldBeDefault)
        {
            //input.IsDefault = true;
            input.ResetToDefault();
        }
        else
        {
            input.Value.Assign(OriginalValue);
            input.IsDefault = false;
        }

        //inputParentSymbol.InvalidateInputInAllChildInstances(input);
        foreach (var instance in symbolChild.Symbol.InstancesOfSelf)
        {
            var inputSlot = instance.Inputs.Single(slot => slot.Id == _inputId);
            inputSlot.DirtyFlag.ForceInvalidate();
        }
            
        if(shouldBeDefault != _wasDefault)
            parentSymbolUi.FlagAsModified();
    }

    private InputValue OriginalValue { get; set; }
    private readonly bool _wasDefault;

    private readonly Guid _inputParentSymbolId;
    private readonly Guid _childId;
    private readonly Guid _inputId;
}