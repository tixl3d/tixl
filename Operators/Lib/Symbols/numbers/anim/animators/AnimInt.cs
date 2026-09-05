using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.anim.animators;

[Guid("85b5a198-deb9-4bea-b760-dad211e16ee6")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed class AnimInt : Instance<AnimInt>
{
    [Output(Guid = "081308EA-FC71-43AE-B0DD-23DE38343AF3", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Result = new();

    [Output(Guid = "e1c176ab-0375-42b2-b222-b971aa6ce889", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasHit = new();

    public AnimInt()
    {
        Result.UpdateAction += Update;
        WasHit.UpdateAction += Update;
    }

    public double _normalizedTime; // only public for Ui

    private void Update(EvaluationContext context)
    {
        if (_lastUpdateFrame == Playback.FrameCount)
            return;

        _lastUpdateFrame = Playback.FrameCount;        
        
        var phase = Phase.GetValue(context);
        var rate = Rate.GetValue(context);
        var rateFactorFromContext = AnimMath.GetSpeedOverrideFromContext(context, AllowSpeedFactor);

        var time = OverrideTime.HasInputConnections
                       ? OverrideTime.GetValue(context)
                       : context.LocalFxTime;

        OverrideTime.DirtyFlag.Clear();

        var originalTime = _normalizedTime;

        _normalizedTime = (time) * rateFactorFromContext * rate + phase;

        var result = (int)_normalizedTime;
        var modulo = Modulo.GetValue(context);
        
        Result.Value = modulo != 0 ? result.Mod(Modulo.GetValue(context))
                                                : result;
        
        WasHit.Value = (int)originalTime != (int)_normalizedTime;
    }

    private int _lastUpdateFrame = -1;

    [Input(Guid = "0D0A2C33-F3BB-4035-8F8D-D01524952B2E")]
    public readonly InputSlot<int> Modulo = new();
    
    [Input(Guid = "e037f3b2-c5eb-4d89-bcbc-ad80aca1cd36")]
    public readonly InputSlot<float> OverrideTime = new();

    [Input(Guid = "1bc7d002-1483-48bd-b419-cccfcb38aa2f")]
    public readonly InputSlot<float> Rate = new();

    [Input(Guid = "5b3d75c9-aac5-44d7-9e68-828cd87cdc0c")]
    public readonly InputSlot<float> Phase = new();

    
    
    [Input(Guid = "72da2976-f1a6-4b3e-a8f0-b43e278e5056", MappedType = typeof(AnimMath.SpeedFactors))]
    public readonly InputSlot<int> AllowSpeedFactor = new();
}