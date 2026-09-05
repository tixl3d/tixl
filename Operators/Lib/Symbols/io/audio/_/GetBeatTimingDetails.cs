using System.Reflection;
using T3.Core.Audio;

namespace Lib.io.audio.@_;

[Guid("712c50e8-7cba-4b29-bde4-1d860ab6b701")]
internal sealed class GetBeatTimingDetails : Instance<GetBeatTimingDetails>
{
        
    //[Output(Guid = "b20573fe-7a7e-48e1-9370-744288ca6e32", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    //public readonly Slot<float> TimeInBars = new Slot<float>();
        
    [Output(Guid = "F697732E-46F3-4037-AFC5-56F396BD70AD", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Dict<float>> Values = new();

    public GetBeatTimingDetails()
    {
        Values.UpdateAction += Update;
    }
        
    private void Update(EvaluationContext context)
    {
        //Log.Debug("Updated beatTappingDetails" + context.LocalFxTime, this);
        var fields = typeof(BeatTimingDetails).GetFields(BindingFlags.Public | BindingFlags.Static);
            
        foreach (var fieldInfo in fields)
        {
            var value = fieldInfo.GetValue(null);
            if (value is float floatValue)
            {
                _details[fieldInfo.Name] = (float)floatValue;
                    
            }
        }
            
        Values.Value = _details;
    }
        
    private readonly Dict<float> _details = new(0f);
}