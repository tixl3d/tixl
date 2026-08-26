using T3.Core.Utils;

namespace Lib.numbers.floats.basic;

[Guid("2ba45de7-c111-4c9f-9d19-8a17130dbcc8")]
internal sealed class SetFloatListValue : Instance<SetFloatListValue>
{
    [Output(Guid = "85bb4be8-b56d-4f00-89e2-89fa111a3636", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Result = new();
        
    public SetFloatListValue()
    {
        Result.UpdateAction = Update;
    }
        
    private void Update(EvaluationContext context)
    {
        var triggerSet = TriggerSet.GetValue(context);
        if (!triggerSet)
            return;
            
        var floatList = FloatList.GetValue(context);
        if (floatList == null || floatList.Count == 0)
            return;
            
        var value = Value.GetValue(context);
        var index = Index.GetValue(context);
            
        if (index >= 0)
        {
            index = index.Mod(floatList.Count);
            switch (Mode.GetEnumValue<Modes>(context))
            {
                case Modes.Set:
                    floatList[index] = value;
                    break;
                    
                case Modes.Add:
                    floatList[index] += value;
                    break;
                    
                case Modes.Multiply:
                    floatList[index] *= value;
                    break;
            }
        }
        else if (index == -2)
        {
            for (var index2 = 0; index2 < floatList.Count; index2++)
            {
                switch (Mode.GetEnumValue<Modes>(context))
                {
                    case Modes.Set:
                        floatList[index2] = value;
                        break;
                        
                    case Modes.Add:
                        floatList[index2] += value;
                        break;
                        
                    case Modes.Multiply:
                        floatList[index2] *= value;
                        break;
                }
                //                    Log.Debug(" Setting...", this);
            }
        }
            
        Result.Value = floatList;
    }
        
    private enum Modes
    {
        Set,
        Add,
        Multiply,
    }
        
    [Input(Guid = "F6EA6675-B0E5-442E-8D6E-6D862B200CCA", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();
        
    [Input(Guid = "2F21B0A8-BC10-4BCB-8545-077A3B8243DF")]
    public readonly InputSlot<bool> TriggerSet = new();
        
    [Input(Guid = "6785B575-14C1-4162-8966-52F1BE93E021")]
    public readonly InputSlot<List<float>> FloatList = new();
        
    [Input(Guid = "E4E6050C-BDF7-48A9-85A4-5557848BDB8B")]
    public readonly InputSlot<int> Index = new();
        
    [Input(Guid = "2FFC3E9F-EF41-4C6A-AAE7-A8E9008A096B")]
    public readonly InputSlot<float> Value = new();
}