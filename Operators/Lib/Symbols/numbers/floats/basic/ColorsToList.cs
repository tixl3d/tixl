namespace Lib.numbers.floats.basic;

[Guid("3a246cf1-22da-4070-8504-08cd1c9c5c40")]
internal sealed class ColorsToList : Instance<ColorsToList>
{
    [Output(Guid = "8DB5DC8B-9521-4A30-9B84-E979D2C34AE7")]
    public readonly Slot<List<Vector4>> Result = new(new List<Vector4>(16));

    public ColorsToList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value.Clear();
        foreach (var input in Colors.GetCollectedTypedInputs())
        {
            Result.Value.Add(input.GetValue(context));
        }
        
        Colors.DirtyFlag.Clear();
    }
        
    [Input(Guid = "011CB37B-EDD1-47FE-8072-7E67C4D5B571")]
    public readonly MultiInputSlot<Vector4> Colors = new();
}