namespace Lib.flow.context;

[Guid("6c1271a0-058f-4ff0-940b-f196e5debdf7")]
internal sealed class GetForegroundColor : Instance<GetForegroundColor>
{
    [Output(Guid = "F962854B-00D6-4EB3-AA4C-E5D4BD500672", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Vector4> Result = new();

    public GetForegroundColor()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = context.ForegroundColor;
    }
}