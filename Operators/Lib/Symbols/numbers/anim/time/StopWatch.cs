using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.anim.time;

[Guid("485af23d-543e-44a7-b29f-693ed9533ab5")]
internal sealed class StopWatch : Instance<StopWatch>
{
    [Output(Guid = "617afbbc-8199-43c0-b630-4563e65959ef", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Delta = new();

    [Output(Guid = "195CDCD3-6F02-471A-96E4-3F44A1D03CC2", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> LastDuration = new();

    public StopWatch()
    {
        LastDuration.UpdateAction += Update;
        Delta.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var resetHit = MathUtils.WasTriggered(ResetTrigger.GetValue(context), ref _wasResetTrigger);

        var runTimeInSecs = Playback.RunTimeInSecs;
        if (resetHit)
        {
            LastDuration.Value = (float)(runTimeInSecs - _startTime);
            _startTime = runTimeInSecs;
            _accumulatedDuration = 0;
        }

        if (context.Playback.PlaybackSpeed != 0)
            _accumulatedDuration += runTimeInSecs - _lastUpdateTime;

        _lastUpdateTime = runTimeInSecs;

        var timeInSecs = PauseWithPlayback.GetValue(context) switch
                             {
                                 false => (float)(runTimeInSecs - _startTime),
                                 true  => _accumulatedDuration
                             };

        Delta.Value = ConvertTime(timeInSecs, DurationIn.GetEnumValue<TimeModes>(context), context.Playback);
        LastDuration.DirtyFlag.Clear();
    }

    private static float ConvertTime(double timeInSecs, TimeModes mode, Playback contextPlayback)
    {
        return mode switch
                   {
                       TimeModes.TimeInSecs => (float)timeInSecs,
                       TimeModes.BeatTime   => (float)contextPlayback.BarsFromSeconds(timeInSecs),
                       _                    => (float)contextPlayback.BarsFromSeconds(timeInSecs)
                   };
    }

    private double _startTime;
    private bool _wasResetTrigger;
    private double _accumulatedDuration;
    private double _lastUpdateTime;

    private enum TimeModes
    {
        TimeInSecs,
        BeatTime,
    }

    [Input(Guid = "38754151-704A-4374-817E-98DFACA62E49")]
    public readonly InputSlot<bool> ResetTrigger = new();

    [Input(Guid = "C19343B2-7534-43A9-A9A6-CE9019437C62", MappedType = typeof(TimeModes))]
    public readonly InputSlot<int> DurationIn = new();

    [Input(Guid = "BF89D8B2-D8FE-4A3C-9255-67944EC831CB")]
    public readonly InputSlot<bool> PauseWithPlayback = new();
}