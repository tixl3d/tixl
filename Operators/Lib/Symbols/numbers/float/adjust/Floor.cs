namespace Lib.numbers.@float.adjust;

[Guid("55b13dee-89f8-404f-b2fe-43d5e8c54536")]
internal sealed class Floor : Instance<Floor>
{
    [Output(Guid = "5c54174b-c9e6-41de-b796-84ef4271dd20")]
    public readonly Slot<float> Result = new();

    public Floor()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = (int)Value.GetValue(context);
    }
        
    [Input(Guid = "550289db-89cb-465c-a9d8-a16dbf23cc45")]
    public readonly InputSlot<float> Value = new();
}