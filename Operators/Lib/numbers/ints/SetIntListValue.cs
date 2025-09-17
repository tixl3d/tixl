using T3.Core.Utils;

namespace Lib.numbers.ints;

[Guid("ba0ce504-a515-437f-a8d5-7889fe50d32c")]
internal sealed class SetIntListValue : Instance<SetIntListValue>
{
    [Output(Guid = "32C0C547-3AD8-4355-A52B-ED110E01A35C", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<int>> Result = new();
        
    public SetIntListValue()
    {
        Result.UpdateAction = Update;
    }
        
    private void Update(EvaluationContext context)
    {
        var triggerSet = TriggerSet.GetValue(context);
        if (!triggerSet)
            return;
            
        var intList = IntList.GetValue(context);
        if (intList == null || intList.Count == 0)
            return;
            
        var value = Value.GetValue(context);
        var index = Index.GetValue(context);
            
        if (index >= 0)
        {
            index = index.Mod(intList.Count);
            switch (Mode.GetEnumValue<Modes>(context))
            {
                case Modes.Set:
                    intList[index] = value;
                    break;
                    
                case Modes.Add:
                    intList[index] += value;
                    break;
                    
                case Modes.Multiply:
                    intList[index] *= value;
                    break;
            }
        }
        else if (index == -2)
        {
            for (var index2 = 0; index2 < intList.Count; index2++)
            {
                switch (Mode.GetEnumValue<Modes>(context))
                {
                    case Modes.Set:
                        intList[index2] = value;
                        break;
                        
                    case Modes.Add:
                        intList[index2] += value;
                        break;
                        
                    case Modes.Multiply:
                        intList[index2] *= value;
                        break;
                }
                //                    Log.Debug(" Setting...", this);
            }
        }
            
        Result.Value = intList;
    }
        
    private enum Modes
    {
        Set,
        Add,
        Multiply,
    }
        
    [Input(Guid = "0CE474D8-57F9-43DA-9374-739738498491", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();
        
    [Input(Guid = "5d42b93a-6973-4d6f-b2bf-3314b67c3a26")]
    public readonly InputSlot<bool> TriggerSet = new();
        
    [Input(Guid = "0F18E03D-C395-4A68-A139-E1E5AA9D54FF")]
    public readonly InputSlot<List<int>> IntList = new();
        
    [Input(Guid = "188C4CD4-4CC9-420A-ACD2-FD0D29E48ED1")]
    public readonly InputSlot<int> Index = new();
        
    [Input(Guid = "E585AF2C-B1B0-4603-B173-AFA452960F29")]
    public readonly InputSlot<int> Value = new();
}