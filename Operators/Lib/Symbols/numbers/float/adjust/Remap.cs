using T3.Core.Utils;

namespace Lib.numbers.@float.adjust;

[Guid("f0acd1a4-7a98-43ab-a807-6d1bd3e92169")]
public sealed class Remap : Instance<Remap>
{
    [Output(Guid = "de6e6f65-cb51-49f1-bb90-34ed1ec963c1")]
    public readonly Slot<float> Result = new();

    public Remap()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var value = Value.GetValue(context);
        var inMin = RangeInMin.GetValue(context);
        var inMax = RangeInMax.GetValue(context);
        var outMin = RangeOutMin.GetValue(context);
        var outMax = RangeOutMax.GetValue(context);
        var biasAndGain = BiasAndGain.GetValue(context);

            
            
        var normalized = (value - inMin) / (inMax - inMin);
        if (normalized > 0 && normalized < 1)
        {
            normalized = normalized.ApplyGainAndBias(biasAndGain.X, biasAndGain.Y);
        }
            
        var v = normalized * (outMax - outMin) + outMin;

        switch ((Modes)Mode.GetValue(context))
        {
            case Modes.Clamped:
            {
                    
                var min = Math.Min(outMin, outMax);
                var max = Math.Max(outMin, outMax);
                v = MathUtils.Clamp(v, min, max);
                break;
            }
            case Modes.Modulo:
            {
                    
                var min = Math.Min(outMin, outMax);
                var max = Math.Max(outMin, outMax);
                v = MathUtils.Fmod(v, max- min);
            }
                break;
        }

        Result.Value = v;
    }

    private enum Modes
    {
        Normal,
        Clamped,
        Modulo,
    }

    [Input(Guid = "40606d4e-acaf-4f23-a845-16f0eb9b73cf")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "edb98f34-d019-47f6-b275-e5a80061e1f7")]
    public readonly InputSlot<float> RangeInMin = new();

    [Input(Guid = "CD369755-5062-4934-8F37-E3A5CC9963DF")]
    public readonly InputSlot<float> RangeInMax = new();

    [Input(Guid = "F2BAF278-ADDE-42DE-AFCE-336B6C8D0387")]
    public readonly InputSlot<float> RangeOutMin = new();

    [Input(Guid = "252276FB-8DE1-42CC-BA41-07D6862015BD")]
    public readonly InputSlot<float> RangeOutMax = new();

    [Input(Guid = "23548048-E373-4FD6-9C83-1CF7398F952D")]
    public readonly InputSlot<Vector2> BiasAndGain = new();

    [Input(Guid = "406F6476-EB25-4493-AAEA-3899E84DE50F", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();        
}