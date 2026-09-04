#nullable enable
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Editor.UiModel.Commands.Animation;

internal sealed class RemoveAnimationsCommand : ICommand
{
    public string Name { get; set; }
    public bool IsUndoable => true;

    internal RemoveAnimationsCommand(Animator animator, IInputSlot[] inputSlots)
    {
        _animator = animator;
        _inputSlots = inputSlots;
        Name = inputSlots.Length == 1 
                   ? $"Remove animation from {inputSlots[0].Parent.Symbol.Name}.{inputSlots[0].Input.Name}" 
                   : $"Remove animation from {inputSlots.Length} parameters";
    }
        
    public void Do()
    {
        var composition = _inputSlots[0].Parent.Parent;
        if (composition == null)
        {
            Log.Error("Failed to remove keyframes");
            return;
        }
        
        foreach (var input in _inputSlots)
        {
            var newCurveSet = new List<Curve>();
            _curveSets.Add(newCurveSet);    
            
            if (!_animator.TryGetCurvesForInputSlot(input, out var curves))
                continue;
            
            foreach (var curve in curves)
            {
                newCurveSet.Add(curve.TypedClone());
            }
            
            _animator.RemoveAnimationFrom(input);
        }
        composition.Symbol.CreateOrUpdateActionsForAnimatedChildren();
    }
        
    public void Undo()
    {
        for (var index = 0; index < _inputSlots.Length; index++)
        {
            var input = _inputSlots[index];
            var curves = _curveSets[index];
            _animator.AddCurvesToInput(curves, input);
        }
    }

    private readonly Animator _animator;
    private readonly IInputSlot[] _inputSlots;
    private readonly List<List<Curve>> _curveSets = new();
}