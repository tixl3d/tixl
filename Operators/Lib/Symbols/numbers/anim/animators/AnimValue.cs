using T3.Core.Utils;

namespace Lib.numbers.anim.animators;

[Guid("ea7b8491-2f8e-4add-b0b1-fd068ccfed0d")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed class AnimValue : Instance<AnimValue>
{
    [Output(Guid = "ae4addf0-08cf-4b25-9515-4fef9359d183", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    [Output(Guid = "5538411F-E6E5-4DFF-9CF4-A6410BE49A8C", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasHit = new();

    public AnimValue()
    {
        Result.UpdateAction += Update;
        WasHit.UpdateAction += Update;
    }

    
    public double _normalizedTime; // only public for Ui
    private AnimMath.Shapes _shape; // only public for Ui 

    private void Update(EvaluationContext context)
    {
        var phase = Phase.GetValue(context);
        var rate = Rate.GetValue(context);
        var ratio = Ratio.GetValue(context);
        var rateFactorFromContext = AnimMath.GetSpeedOverrideFromContext(context, AllowSpeedFactor);
        _shape = (AnimMath.Shapes)Shape.GetValue(context).Clamp(0, Enum.GetNames(typeof(AnimMath.Shapes)).Length);
        _shape = Shape.GetEnumValue<AnimMath.Shapes>(context);
        var amplitude = Amplitude.GetValue(context);
        var offset = Offset.GetValue(context);
        var bias = Bias.GetValue(context);

        var time = OverrideTime.HasInputConnections
                       ? OverrideTime.GetValue(context)
                       : context.LocalFxTime;

        OverrideTime.DirtyFlag.Clear();

        var originalTime = _normalizedTime;

        _normalizedTime = (time) * rateFactorFromContext * rate + phase;
        Result.Value = AnimMath.CalcValueForNormalizedTime(_shape, _normalizedTime, 0, bias, ratio) * amplitude + offset;

        // We need to use evaluation time because outputs flagged as animated and always dirty.
        if (Math.Abs(context.LocalFxTime - _lastUpdateTime) > double.Epsilon)
        {
            _lastUpdateTime = context.LocalFxTime;
            WasHit.Value = (int)originalTime != (int)_normalizedTime;
        }
    }

    private double _lastUpdateTime = double.NegativeInfinity;
    
    [Input(Guid = "7b4992ba-30f7-42e5-b04b-ae4ec0be810e")]
    public readonly InputSlot<float> OverrideTime = new InputSlot<float>();

    [Input(Guid = "4cf5d20b-7335-4584-b246-c260ac5cdf4f", MappedType = typeof(AnimMath.Shapes))]
    public readonly InputSlot<int> Shape = new InputSlot<int>();

    [Input(Guid = "48005727-0158-4795-ad70-8410c27fd01d")]
    public readonly InputSlot<float> Rate = new InputSlot<float>();

    [Input(Guid = "8327e7ec-4370-4a3e-bd69-db3f4aa4b1d7")]
    public readonly InputSlot<float> Ratio = new InputSlot<float>();

    [Input(Guid = "68f14205-f48d-4b44-823d-138eb61767b5")]
    public readonly InputSlot<float> Phase = new InputSlot<float>();

    [Input(Guid = "79917ef7-64ca-4825-9c6a-c9b2a7f6ff86")]
    public readonly InputSlot<float> Amplitude = new InputSlot<float>();

    [Input(Guid = "ddd93b06-118e-43e0-85f6-c150faf91d04")]
    public readonly InputSlot<float> Offset = new InputSlot<float>();

    [Input(Guid = "f12fee9a-dd91-40c2-9aa5-ea34804a858d")]
    public readonly InputSlot<float> Bias = new InputSlot<float>();

    [Input(Guid = "738f6cfb-8b71-423c-b897-824c20397e5a", MappedType = typeof(AnimMath.SpeedFactors))]
    public readonly InputSlot<int> AllowSpeedFactor = new InputSlot<int>();
}