namespace Lib.numbers.@float.adjust;

[Guid("97032147-ba0c-4454-b878-1048d8faea05")]
internal sealed class InvertFloat : Instance<InvertFloat>
{
    [Output(Guid = "b383231e-c952-4b0d-adf3-b97c61c02053")]
    public readonly Slot<float> Result = new();

    public InvertFloat()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var shouldInvert = Invert.GetValue(context);

        var value = A.GetValue(context);
        var sign = shouldInvert ? -1 : 1;
        Result.Value = sign * value;
    }
        
    [Input(Guid = "020acbf3-de2d-48f6-8515-960014bb1aa9")]
    public readonly InputSlot<float> A = new();

    [Input(Guid = "16CECA8F-DD07-4EE9-9BA2-38087E65E802")]
    public readonly InputSlot<bool> Invert = new();
}