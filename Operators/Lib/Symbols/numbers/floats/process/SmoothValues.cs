using T3.Core.Utils;

namespace Lib.numbers.floats.process;

[Guid("50bf9e01-6912-4ab7-b233-660ce67bca25")]
internal sealed class SmoothValues : Instance<SmoothValues>
{
    [Output(Guid = "38FBB674-1EF9-47D0-BDEC-9E1E728F6D92")]
    public readonly Slot<List<float>> Result = new();

    public SmoothValues()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var windowSize = WindowSize.GetValue(context).Clamp(1, 10);
            
        var list = Input.GetValue(context);
        if (list == null || list.Count == 0)
        {
            return;
        }

        if (Result.Value == null || Result.Value.Count != list.Count)
        {
            Result.Value = new List<float>();
        }
            
        Result.Value.Clear();

        for (var index = 0; index < list.Count; index++)
        {
            float sum=0;
            float count=0;
            SampleAtIndex(index, ref sum, ref count);
                
            for (var windowIndex = 0; windowIndex < windowSize; windowIndex++)
            {
                SampleAtIndex(index + windowIndex, ref sum, ref count);
            }

            Result.Value.Add(count == 0 ? float.NaN : sum/count);
        }
            
        void SampleAtIndex(int index, ref float sum, ref float count)
        {
            if (index < 0 || index >= list.Count)
                return;

            sum += list[index];
            count++;
        }
    }
        

        
        
    [Input(Guid = "1f88c433-fd51-4076-8f46-34b586f723f9")]
    public readonly InputSlot<List<float>> Input = new(new List<float>(20));

    [Input(Guid = "da418887-bb4d-4912-b448-13129078fc4c")]
    public readonly InputSlot<int> WindowSize = new ();
        
}