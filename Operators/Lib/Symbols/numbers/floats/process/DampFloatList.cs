using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.floats.process;

[Guid("c30ba288-9e40-4636-beb5-68401d91fe37")]
internal sealed class DampFloatList : Instance<DampFloatList>
{
    [Output(Guid = "23c867c8-d175-463f-bcaa-18e6be5f20c2", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Result = new();


    public DampFloatList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var inputList = Values.GetValue(context);
        var damping = Damping.GetValue(context);

        var currentTime = UseAppRunTime.GetValue(context) ? Playback.RunTimeInSecs : context.LocalFxTime; 
        if (Math.Abs(currentTime - _lastEvalTime) < MinTimeElapsedBeforeEvaluation)
            return;

        // Clean up result list
        if (inputList == null || inputList.Count == 0)
            return;
            
        if (Result.Value == null || Result.Value.Count != inputList.Count)
        {
            Result.Value = new List<float>();
        }
        else
        {
            Result.Value.Clear();
        }
            
        // Clean up internal lists
        MatchListLength(ref _dampedValues, inputList.Count);
        MatchListLength(ref _velocities, inputList.Count);

        _lastEvalTime = currentTime;

        var method = (DampFunctions.Methods)Method.GetValue(context).Clamp(0, 1);

        for(var i = 0; i < inputList.Count; i++)
        {
            var velocity = _velocities[i];
            var value = inputList[i];
            var dampedValue = DampFunctions.DampenFloat(value, _dampedValues[i], damping, ref velocity, method);

            MathUtils.ApplyDefaultIfInvalid(ref dampedValue, 0);
            MathUtils.ApplyDefaultIfInvalid(ref velocity, 0);

            _dampedValues[i] = dampedValue;
            _velocities[i] = velocity;
        }
        Result.Value.AddRange(_dampedValues);
    }

    private static void MatchListLength(ref List<float> list, int length)
    {
        while(list.Count < length)
        {
            list.Add(0);
        }
            
        while(list.Count > length)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    private List<float> _dampedValues = new(1);
    private List<float> _velocities = new(1);
    private double _lastEvalTime;
        
    [Input(Guid = "491cc9cd-28fc-4ec4-8d98-5a7e0d17082a")]
    public readonly InputSlot<List<float>> Values = new(new List<float>(2));

    [Input(Guid = "3d63df3d-81c0-4e27-ae0c-acd3092d952c")]
    public readonly InputSlot<float> Damping = new();
        
    [Input(Guid = "ad97ae08-9aeb-4ed5-b5b1-e24d9af21cf7", MappedType = typeof(DampFunctions.Methods))]
    public readonly InputSlot<int> Method = new();
        
    [Input(Guid = "51bbb9f9-2895-4965-9f73-34d7dbae2ad1")]
    public readonly InputSlot<bool> UseAppRunTime = new();
}