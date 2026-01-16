namespace Lib.numbers.floats.process;

[Guid("136aef9b-373d-487d-9863-a8691d1b909f")]
internal sealed class CompareFloatLists : Instance<CompareFloatLists>
{
    [Output(Guid = "0EC6D73A-098D-426C-8236-ABED8B34D0DE")]
    public readonly Slot<float> Difference = new();

    public CompareFloatLists()
    {
        Difference.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    { 
        //Difference.Value??= [];
        var listA = ListA.GetValue(context);
        var listB = ListB.GetValue(context);

        if (listA == null || listA.Count == 0
                          || listB == null || listB.Count == 0)
        {
            Difference.Value = 1;
            return;
        }

        var threshold = Threshold.GetValue(context);

        var differentElementCount = 0;

        var maxCount = listA.Count > listB.Count ? listA.Count : listB.Count;

        for (int index = 0; index < maxCount; index++)
        {
            if (listA.Count < index || listB.Count < index)
            {
                differentElementCount++;
                continue;
            }

            if (MathF.Abs(listA[index] - listB[index]) > threshold)
            {
                differentElementCount++;
            }
        }

        Difference.Value = (float)differentElementCount / maxCount;

    }
    
    

    [Input(Guid = "3E8C62C0-D981-4B48-96FC-CB7A50AB2A75")]
    public readonly InputSlot<List<float>> ListA = new();
    
    [Input(Guid = "08BA3AE7-18B7-493E-8387-65735BE2DD54")]
    public readonly InputSlot<List<float>> ListB = new();

    [Input(Guid = "0E331C5E-D8A4-4EE8-B552-B6374702649F")]
    public readonly InputSlot<float> Threshold = new();

}