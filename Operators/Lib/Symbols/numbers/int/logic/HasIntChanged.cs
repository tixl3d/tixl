namespace Lib.numbers.@int.logic;

[Guid("62f7408a-a34a-459a-bd7d-bb349196df9b")]
internal sealed class HasIntChanged : Instance<HasIntChanged>
{
    [Output(Guid = "d8ce2d08-4fd3-4a56-92c3-469d661dab8b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasChanged = new();
        

    public HasIntChanged()
    {
        HasChanged.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // Prevent double evaluations
        if (Math.Abs(context.LocalFxTime - _lastEvalTime) < double.Epsilon)
            return;
        
        _lastEvalTime = context.LocalFxTime;
        
        var v = Value.GetValue(context);
        var result = false;
            
        switch ((Modes)ReturnTrueIf.GetValue(context))
        {
            case Modes.Increased:
                result = v > _lastValue;
                break;
                    
            case Modes.Decreased:
                result = v < _lastValue;
                break;
                    
            case Modes.Changed:
                result = v != _lastValue;
                break;
        }
        HasChanged.Value = result;
        _lastValue = v;
    }

    private enum Modes
    {
        Never,
        Increased,
        Decreased,
        Changed
    }

    private int _lastValue;
    private double _lastEvalTime = -1;
        
    [Input(Guid = "A1462674-13D2-4380-8A93-11D0A23DA5AC")]
    public readonly InputSlot<int> Value = new();
        
    [Input(Guid = "B68B0839-156E-4F31-916D-13D066C13831", MappedType = typeof(Modes))]
    public readonly InputSlot<int> ReturnTrueIf = new();

}