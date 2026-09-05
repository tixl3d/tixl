using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.@bool.logic;

[Guid("d21652e9-4dc3-4ece-9205-51fced56c3bd")]
internal sealed class WasTrigger : Instance<WasTrigger>
{
    [Output(Guid = "bbbfa448-3499-4d88-8fe4-0caea4c270fd", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasTriggered = new();
        
    public WasTrigger()
    {
        WasTriggered.UpdateAction += Update;
    }

        
    private void Update(EvaluationContext context)
    {
        if (Math.Abs(Playback.RunTimeInSecs - _lastEvalTime) < 0.010f)
            return;
            
        var triggerIndex = Trigger.GetEnumValue<Triggers>(context);
        _lastEvalTime = Playback.RunTimeInSecs;

        var customVariableName = CustomVariableName.GetValue(context);
            
        var value = 0f;
        switch (triggerIndex)
        {
            case Triggers.None:
                value = 0;
                break;
            case Triggers.TriggerA:
                value = context.FloatVariables.GetValueOrDefault("__TriggerA", 0);
                break;
            case Triggers.TriggerB:
                value = context.FloatVariables.GetValueOrDefault("__TriggerB", 0);
                break;
            case Triggers.Custom:
                    
                value = string.IsNullOrEmpty( customVariableName) 
                            ? 0 
                            : context.FloatVariables.GetValueOrDefault(customVariableName, 0);
                break;
        }

        var increased = (value > _lastValue);
        _lastValue = value;
            
        var triggered = MathUtils.WasTriggered(increased, ref _wasHit);
        WasTriggered.Value = triggered;
    }
        
    private double _lastEvalTime = 0;
    private float _lastValue =0f;
    private bool _wasHit;

    [Input(Guid = "2D78E40B-11F8-4E49-B3A2-5F24F1015B2E", MappedType = typeof(Triggers))]
    public readonly InputSlot<int> Trigger = new();
        
    [Input(Guid = "902D731F-E1B9-40CE-9AFA-5FFB8FED53A4")]
    public readonly InputSlot<string> CustomVariableName = new();



    private enum Triggers
    {
        None,
        TriggerA,
        TriggerB,
        Custom,
    }
}