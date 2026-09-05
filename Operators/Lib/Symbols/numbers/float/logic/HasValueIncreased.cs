namespace Lib.numbers.@float.logic;

[Guid("c513c58d-e45c-408d-a0b8-250c9af31545")]
internal sealed class HasValueIncreased : Instance<HasValueIncreased>
{
    [Output(Guid = "598E38D5-2347-4B93-A7A4-A23190D95DCD")]
    public readonly Slot<bool> HasIncreased = new();
        

    public HasValueIncreased()
    {
        HasIncreased.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var increase = v > _lastValue + Threshold.GetValue(context);
        var hasIncreased = increase; 
        HasIncreased.Value = hasIncreased;
            
        _lastValue = v;
            
    }

    private float _lastValue;
        
    [Input(Guid = "ed88c6c7-1ea2-4593-9589-ec670afb4654")]
    public readonly InputSlot<float> Value = new();
        
    [Input(Guid = "2E43732B-48C4-4EE4-BF92-AF5FAB5B0597")]
    public readonly InputSlot<float> Threshold = new();

}