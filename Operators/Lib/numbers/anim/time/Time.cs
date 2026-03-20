using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.anim.time;

[Guid("b0d75f21-df33-460b-beab-d8c5e1f23e5e")]
internal sealed class Time : Instance<Time>
{
    [Output(Guid = "fd3049aa-4c22-405b-b9b4-0a2474d0e377", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Timefloat = new();

    public Time()
    {
        Timefloat.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var contextLocalTime = (float)context.LocalTime;
        var contextLocalFxTime = (float)context.LocalFxTime;

        var timeMode = Mode.GetEnumValue<TimeModes>(context);
        var speedFactor = SpeedFactor.GetValue(context);

        // Disable dirty flagging
        var isFrozen = timeMode == TimeModes.Frozen;
        if (!_isFirstEval && isFrozen != _isFrozen)
        {
            Timefloat.DirtyFlag.Trigger = isFrozen 
                                              ? DirtyFlagTrigger.None
                                              : DirtyFlagTrigger.Animated;
            _isFrozen = isFrozen;
        }

        
        var time = timeMode switch
                       {
                           TimeModes.LocalIdleMotionFxTime => contextLocalFxTime,
                           TimeModes.LocalTime             => contextLocalTime,
                           TimeModes.PlaybackTime          => (float)context.Playback.TimeInBars,
                           TimeModes.Runtime               => context.Playback.BarsFromSeconds(Playback.RunTimeInSecs),
                           TimeModes.Frozen                => 0,
                           _                               => throw new ArgumentOutOfRangeException()
                       };

        if (Units.GetValue(context) == 1)
        {
            Timefloat.Value = (float)context.Playback.SecondsFromBars(time * speedFactor);
        }
        else
        {
            Timefloat.Value = (float)(time * speedFactor);
        }

        _isFirstEval = false;
    }

    private bool _isFrozen;

    private enum TimeModes
    {
        LocalIdleMotionFxTime,
        LocalTime,
        PlaybackTime,
        Runtime,
        Frozen,
    }

    private enum TimeUnits
    {
        Bars,
        Secs,
    }

    private bool _isFirstEval = true;

    [Input(Guid = "e0f765f3-ac71-45c9-87a4-e9be3fa9a9a0")]
    public readonly InputSlot<float> SpeedFactor = new();

    [Input(Guid = "6d2f783a-23b7-425c-a4e3-cfcdcd61cf3a", MappedType = typeof(TimeModes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "BA443B6A-487F-4739-94A3-915584EE2D46", MappedType = typeof(TimeUnits))]
    public readonly InputSlot<int> Units = new();
}