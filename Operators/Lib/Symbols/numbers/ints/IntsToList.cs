namespace Lib.numbers.ints;

[Guid("6095a1f7-0633-4a6b-8eb0-331db20a814e")]
internal sealed class IntsToList : Instance<IntsToList>
{
    [Output(Guid = "5FFB80D9-BC58-4483-AE15-D874F6270AB8")]
    public readonly Slot<List<int>> Result = new(new List<int>(20));

    public IntsToList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value.Clear();
        foreach (var input in Input.GetCollectedTypedInputs())
        {
            Result.Value.Add(input.GetValue(context));
        }
        
        Input.DirtyFlag.Clear();
    }
        
    [Input(Guid = "2BE3761C-D07C-408B-91F4-17088BB19FB8")]
    public readonly MultiInputSlot<int> Input = new();
}