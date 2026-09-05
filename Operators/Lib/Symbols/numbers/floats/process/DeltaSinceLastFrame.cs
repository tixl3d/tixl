namespace Lib.numbers.floats.process;

[Guid("919beb6b-4798-45fc-83bd-8203a9b26e8e")]
internal sealed class DeltaSinceLastFrame : Instance<DeltaSinceLastFrame>
{
    [Output(Guid = "F2E21170-11CE-4FEE-BA2C-80AC2CDCE11A", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Change = new();
        
    public DeltaSinceLastFrame()
    {
        Change.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Math.Abs(context.Playback.FxTimeInBars - _lastEvalTime) < 0.0001)
            return;
        _lastEvalTime = context.Playback.FxTimeInBars;
            
        var v = Value.GetValue(context);
        var delta = v - _lastValue;
        _lastValue = v;
        Change.Value = delta;
    }

    private float _lastValue = 0;
    private double _lastEvalTime;

    [Input(Guid = "0e8896e1-b98f-4ff3-9136-e55002c887d8")]
    public readonly InputSlot<float> Value = new();
        
    [Input(Guid = "883f16e2-b4fc-4640-b5fe-6e39c7d3f28c")]
    public readonly InputSlot<float> Threshold = new();

}