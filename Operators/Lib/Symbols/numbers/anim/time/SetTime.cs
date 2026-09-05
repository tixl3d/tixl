using T3.Core.Utils;

namespace Lib.numbers.anim.time;

[Guid("32325c5b-53f7-4414-b4dd-a436e45528b0")]
internal sealed class SetTime : Instance<SetTime>
{
       
    [Output(Guid = "FE01C3B6-72E2-494E-8511-6D50C527463F")]
    public readonly Slot<Command> Result = new();

        
    public SetTime()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var newTime = NewTime.GetValue(context);
        var mode = OffsetMode.GetEnumValue<Modes>(context);

        if (SubTree.HasInputConnections)
        {
            var previousKeyframeTime = context.LocalTime;
            var previousEffectTime = context.LocalFxTime;

            if (mode == Modes.Absolute)
            {
                context.LocalTime = newTime;
                context.LocalFxTime = newTime;
            }
            else
            {
                context.LocalTime += newTime;
                context.LocalFxTime += newTime;
            }
                
            // Execute subtree
            Result.Value = SubTree.GetValue(context);
            context.LocalTime = previousKeyframeTime;
            context.LocalFxTime = previousEffectTime;
        }
        else if(mode == Modes.GlobalAbsolute)
        {
            context.LocalTime = newTime;
            context.LocalFxTime = newTime;
        }
    }
        
    [Input(Guid = "01F2EEF1-E3C1-49A5-B532-9C12DA8CAAC5")]
    public readonly InputSlot<Command> SubTree = new();
        
    [Input(Guid = "d2c934bb-de5c-449c-adf8-7a2f48082e9c")]
    public readonly InputSlot<float> NewTime = new();
        
    [Input(Guid = "EFA8C97D-769C-4A26-B442-E702AC74A1F4", MappedType = typeof(Modes))]
    public readonly InputSlot<int> OffsetMode = new();

    private enum Modes
    {
        Absolute,
        Relative,
        GlobalAbsolute,
    }
}