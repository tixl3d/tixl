using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.anim.animators;

[Guid("1f85f846-0a59-44f3-8e3e-4c2357893494")]
public sealed class AnimBoolean : Instance<AnimBoolean>
{
    [Output(Guid = "4b2820f8-f7e5-4973-81f6-7fa6b69d0509", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> TriggerOutput = new();
    
    public AnimBoolean()
    {
        TriggerOutput.UpdateAction += Update;
    }
    
    
    private void Update(EvaluationContext context)
    {
        if (_lastUpdateFrame == Playback.FrameCount)
            return;

        _lastUpdateFrame = Playback.FrameCount;

        var time = context.LocalFxTime;
        var rateFactorFromContext = AnimMath.GetSpeedOverrideFromContext(context, AllowSpeedFactor);
        var originalTime = _normalizedTime;

        _normalizedTime = time * rateFactorFromContext 
                                 * Rate.GetValue(context) 
                          + Phase.GetValue(context);

        
        // We need to use evaluation time because outputs flagged as animated and always dirty.
        TriggerOutput.Value = (int)originalTime != (int)_normalizedTime;
    }

    
    // Public for opUi
    public double _normalizedTime;
    private int _lastUpdateFrame = -1;
        
    [Input(Guid = "a8e49df7-3388-4532-8efe-766ea3a47108")]
    public readonly InputSlot<float> Rate = new();
    
    [Input(Guid = "CFA3D623-F655-4386-8458-2FBD273A2B88")]
    public readonly InputSlot<float> Phase = new InputSlot<float>();

    
    [Input(Guid = "BE941605-4AF3-445E-93A6-C830EEBB3ED5", MappedType = typeof(AnimMath.SpeedFactors))]
    public readonly InputSlot<int> AllowSpeedFactor = new InputSlot<int>();
}