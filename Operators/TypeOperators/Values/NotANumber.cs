namespace Types.Values;

[Guid("b0042ec2-dbfd-40b9-b8dc-6a4b3630d3ab")]
public sealed class NotANumber : Instance<NotANumber>
{
    [Output(Guid = "caed8758-f20f-4be7-9861-b6ec90e816d2")]
    public readonly Slot<float> Result = new();

    public NotANumber()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = float.NaN;
    }
}