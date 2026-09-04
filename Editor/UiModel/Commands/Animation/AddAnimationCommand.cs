#nullable enable
using System.Diagnostics;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Editor.UiModel.Commands.Animation;

//TODO: ideally this should not hold references
internal sealed class AddAnimationCommand : ICommand
{
    public string Name { get; set; }
    public bool IsUndoable => true;

    internal AddAnimationCommand(Animator animator, IInputSlot inputSlot)
    {
        _animator = animator;
        _inputSlot = inputSlot;
        Name = $"Add animation to {inputSlot.Parent.Symbol.Name}.{inputSlot.Input.Name}";
    }
        
    public void Do()
    {
        var composition = _inputSlot.Parent.Parent;
        Debug.Assert(composition != null);

        _wasDefault = _inputSlot.Input.IsDefault; 
        _keepCurves = _animator.AddOrRestoreCurvesToInput(_inputSlot, _keepCurves);
        composition.Symbol.CreateOrUpdateActionsForAnimatedChildren();
    }
        
    public void Undo()
    {
        var composition = _inputSlot.Parent.Parent;
        Debug.Assert(composition != null);
        
        _animator.RemoveAnimationFrom(_inputSlot);
        if (_wasDefault)
            _inputSlot.Input.IsDefault = true;

        composition.Symbol.CreateOrUpdateActionsForAnimatedChildren();
    }

    private readonly Animator _animator;
    private readonly IInputSlot _inputSlot;
    private bool _wasDefault;
    private Curve[]? _keepCurves;
}