using T3.Core.Utils;

namespace Lib.numbers.anim.animators;

[Guid("7814fd81-b8d0-4edf-b828-5165f5657344")]
public sealed class AnimVec3 : Instance<AnimVec3>
{
    [Output(Guid = "A77BAA35-3CD9-44D3-9F75-2A4F95FBD595", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Result = new();
        
    public AnimVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var phases = Phases.GetValue(context);
        var masterRate = RateFactor.GetValue(context);
        var rates = Rates.GetValue(context);
        var ratio = Ratio.GetValue(context);
        var rateFactorFromContext = AnimMath.GetSpeedOverrideFromContext(context, AllowSpeedFactor);
        _shape = (AnimMath.Shapes)Shape.GetValue(context).Clamp(0, Enum.GetNames(typeof(AnimMath.Shapes)).Length);
        var amplitudeFactor = AmplitudeFactor.GetValue(context);
        var amplitudes = Amplitudes.GetValue(context) * amplitudeFactor;
        var offsets = Offsets.GetValue(context);
        var bias = Bias.GetValue(context);
        var time = OverrideTime.HasInputConnections
                       ? OverrideTime.GetValue(context)
                       : context.LocalFxTime;

        // Don't use vector to keep double precision
        _normalizedTimeX = (time + phases.X) * masterRate * rateFactorFromContext * rates.X;
        _normalizedTimeY = (time + phases.Y) * masterRate * rateFactorFromContext * rates.Y;
        _normalizedTimeZ = (time + phases.Z) * masterRate * rateFactorFromContext * rates.Z;

        Result.Value 
            = new Vector3( AnimMath.CalcValueForNormalizedTime(_shape, _normalizedTimeX, 0, bias, ratio) * amplitudes.X + offsets.X,
                           AnimMath.CalcValueForNormalizedTime(_shape, _normalizedTimeY, 1, bias, ratio) * amplitudes.Y + offsets.Y,
                           AnimMath.CalcValueForNormalizedTime(_shape, _normalizedTimeZ, 2, bias, ratio) * amplitudes.Z + offsets.Z);
    }

    [Input(Guid = "fa1b36a1-4ed9-4187-aeb6-f7ba893cf3b2")]
    public readonly InputSlot<float> OverrideTime = new();

    [Input(Guid = "c8faaeca-c153-4d7c-a66b-6916dc7750e3", MappedType = typeof(AnimMath.Shapes))]
    public readonly InputSlot<int> Shape = new();

    [Input(Guid = "9F71C196-4C4C-4083-8C27-3047C059F998")]
    public readonly InputSlot<Vector3> Rates = new();

    [Input(Guid = "754456d0-1ac8-4e31-8d0b-bf1b45db48de")]
    public readonly InputSlot<float> RateFactor = new();

    [Input(Guid = "A04BB5A4-A7AF-493B-8509-4E9C8B6A94A9")]
    public readonly InputSlot<Vector3> Phases = new();

    [Input(Guid = "AAD62703-647D-437B-879B-08793AC8802F")]
    public readonly InputSlot<Vector3> Amplitudes = new();

    [Input(Guid = "6086F33D-E8AA-4F66-830E-8624E19E186C")]
    public readonly InputSlot<float> AmplitudeFactor = new();

    [Input(Guid = "2384604D-6563-4AF7-8799-9666A6D94171")]
    public readonly InputSlot<Vector3> Offsets = new();

    [Input(Guid = "42ABBD5C-5AE3-41C1-BEBD-CE19D2CE0E25")]
    public readonly InputSlot<float> Bias = new();

    [Input(Guid = "FF71A701-802F-44EC-966D-4E04557CDBE6")]
    public readonly InputSlot<float> Ratio = new();

    [Input(Guid = "2d400a08-8926-46bd-b9ba-75ec69fec9dd", MappedType = typeof(AnimMath.SpeedFactors))]
    public readonly InputSlot<int> AllowSpeedFactor = new();

    public double _normalizedTimeX;
    public double _normalizedTimeY;
    public double _normalizedTimeZ;
    public AnimMath.Shapes _shape;
}