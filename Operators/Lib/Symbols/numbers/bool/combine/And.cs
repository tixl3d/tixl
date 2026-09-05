namespace Lib.numbers.@bool.combine;

[Guid("a18ae2d3-1735-40b8-bebb-65a6788bc872")]
internal sealed class And : Instance<And>
{
    [Output(Guid = "c02d701d-a915-4d2e-bb31-5c6cd27a999e")]
    public readonly Slot<bool> Result = new();

    public And()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = A.GetValue(context) & B.GetValue(context);
    }
        
    [Input(Guid = "1931b0fe-0df0-4ba1-9da5-b3eceaa87888")]
    public readonly InputSlot<bool> A = new();

    [Input(Guid = "af89954f-9f79-4782-95ab-f40bb50339c8")]
    public readonly InputSlot<bool> B = new();
}