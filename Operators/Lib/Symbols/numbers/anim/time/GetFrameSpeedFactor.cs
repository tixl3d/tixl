namespace Lib.numbers.anim.time;

[Guid("3be62864-30dd-4531-9980-28b634296e47")]
internal sealed class GetFrameSpeedFactor : Instance<GetFrameSpeedFactor>
{
    [Output(Guid = "d5de5667-82f7-4556-9b44-5e6ebdcfdd4d", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> FrameSpeedFactor = new();
        
    public GetFrameSpeedFactor()
    {
        FrameSpeedFactor.UpdateAction = Update;
            
    }

    private void Update(EvaluationContext context)
    {
        var isValid = Math.Abs(context.Playback.FrameSpeedFactor) > 0.0001;
        FrameSpeedFactor.Value= isValid ? (float)context.Playback.FrameSpeedFactor:
                                    1;
    }
}