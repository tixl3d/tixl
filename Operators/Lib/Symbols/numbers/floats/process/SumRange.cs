namespace Lib.numbers.floats.process;

[Guid("d3a19896-230f-458f-b4ba-e448f63f0d51")]
internal sealed class SumRange : Instance<SumRange>
{
    [Output(Guid = "3bfdd07b-8212-424d-b449-f7b87551410f")]
    public readonly Slot<float> Selected = new();

    public SumRange()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = Input.GetValue(context);
        if (list == null || list.Count == 0)
        {
            return;
        }
        var lowerLimit = Math.Max(0, LowerLimit.GetValue(context));
        var upperLimit = Math.Min(list.Count, UpperLimit.GetValue(context));
        var sum = 0f;
        for (var index = lowerLimit; index < upperLimit; index++) {
            sum += list[index];
        }
        Selected.Value = sum;
    }


    [Input(Guid = "865d1fd4-b225-4651-bc01-c43f071b8b42")]
    public readonly InputSlot<int> LowerLimit = new(0);
        
    [Input(Guid = "646E17F6-5A98-42C7-8D3B-26CCB02D3E68")]
    public readonly InputSlot<int> UpperLimit = new(0);
        
    [Input(Guid = "056eba13-3ea9-4d0f-a45d-fce1ffaf403c")]
    public readonly InputSlot<List<float>> Input = new(new List<float>(20));
}