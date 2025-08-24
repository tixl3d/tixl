using T3.Core.Animation;
using T3.Core.Utils;
// ReSharper disable InconsistentNaming

namespace Lib.numbers.@bool.process;

[Guid("fbd9ac37-4427-4852-95d7-d8383fefbe36")]
internal sealed class DelayTriggerChange : Instance<DelayTriggerChange>
{
    [Output(Guid = "04febb7c-e4c8-4252-9606-be433f82c8ad", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> DelayedTrigger = new();

    [Output(Guid = "8b26b8c1-47ae-4ed2-bec6-1dffb077d553", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> RemainingTime = new();

        
    public DelayTriggerChange()
    {
        DelayedTrigger.UpdateAction += Update;
        RemainingTime.UpdateAction += Update;
    }
        
    private double _lastFalseTime;
    private double _lastTrueTime;
    private double _lastChangeTime;

    private bool _triggered;
    private bool _stateBeforeChange;
        
    private void Update(EvaluationContext context)
    {
        var isTriggered = Trigger.GetValue(context);

        var hasBeenChanged = isTriggered != _triggered;
        _triggered = isTriggered;
            
        var delayDuration = DelayDuration.GetValue(context);

        var delayMode = Mode.GetEnumValue<DelayModes>(context);
        var timeMode = TimeMode.GetEnumValue<TimeModes>(context);

        var currentTime = timeMode switch
                              {
                                  TimeModes.LocalFxTime_InBars => context.LocalFxTime,
                                  TimeModes.LocalFxTime_InSecs => context.Playback.SecondsFromBars(context.LocalFxTime),
                                  TimeModes.LocalTime_InBars   => context.LocalTime,
                                  TimeModes.LocalTime_InSecs   => context.Playback.SecondsFromBars(context.LocalTime),
                                  TimeModes.PlayTime_InBars    => context.Playback.TimeInBars,
                                  TimeModes.PlayTime_InSecs    => context.Playback.TimeInSecs,
                                  TimeModes.AppRunTime_InSecs  => Playback.RunTimeInSecs,
                                  _                            => 0
                              };

        if (isTriggered)
        {
            _lastTrueTime = currentTime;
        }
        else
        {
            _lastFalseTime = currentTime;
        }

            
        if (hasBeenChanged)
        {
            _lastChangeTime = currentTime;
            _stateBeforeChange = DelayedTrigger.Value;
        }
            
        double refTime = 0;
        var stateIfDelayed = false; 
            
        switch (delayMode)
        {
            case DelayModes.DelayTrue:
                refTime = _lastTrueTime;
                stateIfDelayed = true;
                break;
            case DelayModes.DelayFalse:
                stateIfDelayed = false;
                refTime = _lastFalseTime;
                break;
            case DelayModes.DelayBoth:
                stateIfDelayed = _stateBeforeChange;
                refTime = _lastChangeTime;
                break;
        }

            
        var remainingTime = refTime -currentTime +  delayDuration;
        var isDelayed = remainingTime > 0;

        RemainingTime.Value = (float)remainingTime;
        DelayedTrigger.Value = isDelayed ? stateIfDelayed :  _triggered;
    }

    [Input(Guid = "AB225218-F4B6-4AB6-BE28-F2526DA4B1A0")]
    public readonly InputSlot<bool> Trigger = new();

    [Input(Guid = "bfe68cee-b0f2-4140-931a-c8e089f7e2c2")]
    public readonly InputSlot<float> DelayDuration = new();

    [Input(Guid = "da8498e1-a466-435b-9750-80a751dea6c6", MappedType = typeof(DelayModes))]
    public readonly InputSlot<int> Mode = new();
        
    [Input(Guid = "1D86C7D6-95C1-416C-A643-28745E099A53", MappedType = typeof(TimeModes))]
    public readonly InputSlot<int> TimeMode = new();

        
    private enum DelayModes
    {
        DelayTrue,
        DelayFalse,
        DelayBoth,
    }

    private enum TimeModes
    {
        LocalFxTime_InBars,
        LocalFxTime_InSecs,
        LocalTime_InBars,
        LocalTime_InSecs,
        PlayTime_InBars,
        PlayTime_InSecs,
        AppRunTime_InSecs,
    }
}