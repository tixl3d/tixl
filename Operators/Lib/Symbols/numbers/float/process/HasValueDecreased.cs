namespace Lib.numbers.@float.process;

[Guid("f376121a-2360-4232-9724-0db6937062c3")]
internal sealed class HasValueDecreased : Instance<HasValueDecreased>
{
    [Output(Guid = "2de049e8-77d3-4f01-9ba2-63ddeee935ba")]
    public readonly Slot<bool> HasDecreased = new();
        

    public HasValueDecreased()
    {
        HasDecreased.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var decrease = v < _lastValue - Threshold.GetValue(context);
        var hasDecreased = decrease; 
        HasDecreased.Value = hasDecreased;

        _lastValue = v;
    }
    
    private float _lastValue = 0;
        
    [Input(Guid = "0ce24e8e-7d35-41a1-85a5-0c55d4247a90")]
    public readonly InputSlot<float> Value = new();
        
    [Input(Guid = "332d2377-c5d3-448d-851f-26e3439720dc")]
    public readonly InputSlot<float> Threshold = new();

}