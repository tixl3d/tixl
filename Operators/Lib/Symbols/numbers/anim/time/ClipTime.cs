namespace Lib.numbers.anim.time;

[Guid("83b8fc42-a200-4c3d-85dc-035b4f478069")]
internal sealed class ClipTime : Instance<ClipTime>
{
    [Output(Guid = "3d9050a0-5688-4315-a6a4-fd8f1613eae2", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> Time = new();

    public ClipTime()
    {
        Time.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
            
        Time.Value = (float)context.LocalTime;
    }
}