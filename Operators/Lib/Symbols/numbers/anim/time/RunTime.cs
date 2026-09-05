using T3.Core.Animation;

namespace Lib.numbers.anim.time;

[Guid("862de1a8-f630-4823-8860-7afa918bb1bc")]
internal sealed class RunTime : Instance<RunTime>
{
    [Output(Guid = "{1C34D39C-0BEF-4C4A-A3E4-DCB8D5664F3B}", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> TimeInSeconds = new();

    public RunTime()
    {
        TimeInSeconds.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TimeInSeconds.Value = (float)Playback.RunTimeInSecs;
    }
}