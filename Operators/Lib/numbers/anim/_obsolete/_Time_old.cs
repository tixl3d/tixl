using T3.Core.Utils;

namespace Lib.numbers.anim._obsolete;

[Guid("9cb4d49e-135b-400b-a035-2b02c5ea6a72")]
internal sealed class _Time_old : Instance<_Time_old>
{
    [Output(Guid = "b20573fe-7a7e-48e1-9370-744288ca6e32", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> TimeInBars = new();

    [Output(Guid = "A606B326-F3AF-470B-B6E5-3175F7A54E31", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> TimeInSecs = new();

        
    public _Time_old()
    {
        TimeInBars.UpdateAction += Update;
        TimeInSecs.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var contextLocalTime = (float)context.LocalTime;
        var contextLocalFxTime = (float)context.LocalFxTime;

        var time = Mode.GetEnumValue<Modes>(context) switch
                       {
                           Modes.LocalFxTimeInBars  => contextLocalFxTime,
                           Modes.LocalTimeInBars    => contextLocalTime,
                           Modes.LocalTimeInSecs    => contextLocalTime * 240 / (float)context.Playback.Bpm,
                           Modes.PlaybackTimeInBars => (float)context.Playback.TimeInBars,
                           _                        => throw new ArgumentOutOfRangeException()
                       };

        TimeInBars.Value = time * SpeedFactor.GetValue(context);
        TimeInSecs.Value = (float)context.Playback.TimeInSecs * SpeedFactor.GetValue(context);
    }


    private enum Modes
    {
        LocalFxTimeInBars,
        LocalTimeInBars,
        LocalTimeInSecs,
        PlaybackTimeInBars,
    }
        
    [Input(Guid = "8DA7D58D-10A5-4378-8F44-B98F87EC2697", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();
        
        
    [Input(Guid = "2d9c040d-5244-40ac-8090-d8d57323487b")]
    public readonly InputSlot<float> SpeedFactor = new();
}