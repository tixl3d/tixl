using T3.Core.Animation;

namespace Lib.numbers.anim.time;

[Guid("5af2405c-35f4-46bf-90db-bb99b0c4a43e")]
internal sealed class LastFrameDuration : Instance<LastFrameDuration>
{
    [Output(Guid = "04c5cc91-5cfd-4ef5-9dd9-42cb048ce9b5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Duration = new();
        
        
    public LastFrameDuration()
    {
        Duration.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Duration.Value = (float)Playback.LastFrameDuration;
    }
}