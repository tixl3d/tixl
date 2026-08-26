using T3.Core.Utils;

namespace Lib.numbers.@bool.process;

[Guid("743e18c7-eed5-40df-9a86-7caa996d94a2")]
internal sealed class KeepBoolean : Instance<KeepBoolean>
{
    [Output(Guid = "7CE44DDD-369A-48EE-AC25-8D1A57D1021F")]
    public readonly Slot<bool> Result = new();
        
    [Output(Guid = "3ee8c143-604f-48d1-bea7-4dbe13835800", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> TimeSinceFreeze = new();
        
    public KeepBoolean()
    {
        Result.UpdateAction += Update;
        TimeSinceFreeze.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var newValue = Value.GetValue(context);
        var freeze = Freeze.GetValue(context);
        var mode = Mode.GetEnumValue<Modes>(context);
        var wasTriggered = MathUtils.WasTriggered(freeze, ref _freeze);

        if (wasTriggered)
        {
            _freezeTime = context.LocalTime;
        }
            
        if (mode == Modes.FreezeWhileTrue)
        {
            if (!freeze)
            {
                _frozenValue = newValue;
            }
        }
        else
        {
            if (wasTriggered)
            {
                _frozenValue = newValue;
                    
            }
        }
            
        Result.Value = _frozenValue;
        TimeSinceFreeze.Value = (float)(context.LocalTime - _freezeTime);
    }

    private bool _frozenValue;
    private bool _freeze;
    private double _freezeTime;

    [Input(Guid = "2838DEC7-502B-4761-BCA7-7389BDB69504")]
    public readonly InputSlot<bool> Value = new();

        
    [Input(Guid = "5a571583-7544-4a79-9211-ab1225260e71")]
    public readonly InputSlot<bool> Freeze = new();
        
        
    [Input(Guid = "2fa4755e-1e0b-4458-a53d-c455c10ae9a6", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    private enum Modes
    {
        FreezeWhileTrue,
        UpdateWhenSwitchingToTrue,
    }
}