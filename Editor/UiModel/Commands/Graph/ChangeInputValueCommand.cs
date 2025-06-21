using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Editor.UiModel.Commands.Graph;

public sealed class ChangeInputValueCommand : ICommand
{
    public string Name => "Change Input Value";
    public bool IsUndoable => true;

    public ChangeInputValueCommand(Symbol composition, Guid symbolChildId, Symbol.Child.Input input, InputValue newValue)
    {
        _inputParentSymbol = composition;
        _inputParentSymbolId = composition.Id;
            
        _childId = symbolChildId;
        _inputId = input.InputDefinition.Id;
        _wasAnimated = composition.Animator.IsAnimated(_childId, _inputId);
        _wasDefault = input.IsDefault;
        _animationTime = Playback.Current.TimeInBars;
        OriginalValue = input.Value.Clone();
        _newValue = newValue == null ? input.Value.Clone() : newValue.Clone();
            
        if (_wasAnimated)
        {
            _originalKeyframes = composition.Animator.GetTimeKeys(_childId, _inputId, _animationTime).ToList();
        }
    }

    public void Undo()
    {
        if(!SymbolUiRegistry.TryGetSymbolUi(_inputParentSymbolId, out var inputParentSymbolUi))
            throw new Exception("Symbol not found: " + _inputParentSymbolId);
            
        var inputParentSymbol = inputParentSymbolUi.Symbol;
        if (_wasAnimated)
        {
            var hasNewKeyframes = false;
            foreach (var v in _originalKeyframes)
            {
                if (v == null)
                {
                    hasNewKeyframes = true;
                    break;
                }
            }

            var animator = inputParentSymbol.Animator;
            if (hasNewKeyframes) 
            {
                // todo: these are identical?
            }

            animator.SetTimeKeys(_childId, _inputId,_animationTime, _originalKeyframes); // TODO: Remove keyframes

            var symbolChild = inputParentSymbol.Children[_childId];
                
            InvalidateInstances(inputParentSymbol, symbolChild);
        }
        else
        {
            if (_wasDefault)
            {
                if (!inputParentSymbol.Children.TryGetValue(_childId, out var symbolChild))
                    return;
                    
                var input = symbolChild.Inputs[_inputId];
                input.ResetToDefault();
                InvalidateInstances(inputParentSymbol, symbolChild);
            }
            else
            {
                AssignValue(OriginalValue);
            }
        }
        inputParentSymbolUi.FlagAsModified();
    }

    public void Do()
    {
        AssignValue(_newValue);
    }
        
    public void AssignNewValue(InputValue valueToSet)
    {
        _newValue.Assign(valueToSet);
        AssignValue(valueToSet);
    }

    private void AssignValue(InputValue valueToSet)
    {
        if(!SymbolUiRegistry.TryGetSymbolUi(_inputParentSymbolId, out var inputParentSymbolUi))
            throw new Exception("Symbol not found: " + _inputParentSymbolId);
            
        var inputParentSymbol = inputParentSymbolUi.Symbol;
            
        if (!inputParentSymbol.Children.TryGetValue(_childId, out var symbolChild))
        {
            // This can happen if blended instances are deleted
            Log.Debug($"Can't assign value to missing symbolChild {_childId}");
            return;
        }
        var input = symbolChild.Inputs[_inputId];

        if (!SymbolUiRegistry.TryGetSymbolUi(symbolChild.Symbol.Id, out var symbolUi))
        {
            Log.Warning($"Can't find symbol child's SymbolUI  {symbolChild.Symbol.Id} - was it removed? [{symbolChild.Symbol.Name}]");
            return;
        }

        if (_wasAnimated)
        {
            var inputUi = symbolUi.InputUis[_inputId];
            var animator = inputParentSymbol.Animator;
            var symbolChildId = symbolChild.Id;

            foreach (var parentInstance in inputParentSymbol.InstancesOfSelf)
            {
                var instance = parentInstance.Children[symbolChildId];
                var inputSlot = instance.Inputs.Single(slot => slot.Id == _inputId);
                inputUi.ApplyValueToAnimation(inputSlot, valueToSet, animator, _animationTime);
                inputSlot.DirtyFlag.ForceInvalidate();
            }
        }
        else
        {
            input.IsDefault = false;
            input.Value.Assign(valueToSet);
            InvalidateInstances(inputParentSymbol, symbolChild);
        }
        
        inputParentSymbolUi.FlagAsModified();
    }

    private void InvalidateInstances(Symbol inputParentSymbol, Symbol.Child symbolChild)
    {
        var symbolChildId = symbolChild.Id;
        foreach (var parentInstance in inputParentSymbol.InstancesOfSelf)
        {
            var instance = parentInstance.Children[symbolChildId];
            var inputSlot = instance.Inputs.Single(slot => slot.Id == _inputId);
            inputSlot.DirtyFlag.ForceInvalidate();
        }
    }

    private InputValue OriginalValue { get; set; }
    private readonly InputValue _newValue;
    private readonly Guid _inputParentSymbolId;
    private readonly Symbol _inputParentSymbol;
    private readonly Guid _childId;
    private readonly Guid _inputId;
    private readonly bool _wasDefault;
    private readonly bool _wasAnimated;
    private readonly double _animationTime;
    private readonly List<VDefinition> _originalKeyframes;
}