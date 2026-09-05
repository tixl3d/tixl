using T3.Core.Utils;

namespace Lib.numbers.anim.animators;

[Guid("af79ee8c-d08d-4dca-b478-b4542ed69ad8")]
public sealed class AnimVec2 : Instance<AnimVec2>
{
    [Output(Guid = "7757A3F5-EA71-488E-9CEC-0151FFD332CC", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector2> Result = new();

    public AnimVec2()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var phases = Phases.GetValue(context);
        var masterRate = RateFactor.GetValue(context);
        var rates = Rates.GetValue(context);
        var rateFactorFromContext = AnimMath.GetSpeedOverrideFromContext(context, AllowSpeedFactor);
        _shape = (AnimMath.Shapes)Shape.GetValue(context).Clamp(0, Enum.GetNames(typeof(AnimMath.Shapes)).Length);
        var amplitudeFactor = AmplitudeFactor.GetValue(context);
        var amplitudes = Amplitudes.GetValue(context) * amplitudeFactor;
        var offsets = Offsets.GetValue(context);
        var bias = Bias.GetValue(context);
        var ratio = Ratio.GetValue(context);
        var time = OverrideTime.HasInputConnections
                       ? OverrideTime.GetValue(context)
                       : context.LocalFxTime;

        // Don't use vector to keep double precision
        _normalizedTimeX = (time + phases.X) * masterRate * rateFactorFromContext * rates.X;
        _normalizedTimeY = (time + phases.Y) * masterRate * rateFactorFromContext * rates.Y;

        Result.Value = new Vector2(AnimMath.CalcValueForNormalizedTime(_shape, _normalizedTimeX, 0, bias, ratio) * amplitudes.X + offsets.X,
                                   AnimMath.CalcValueForNormalizedTime(_shape, _normalizedTimeY, 1, bias, ratio) * amplitudes.Y + offsets.Y);
    }

    [Input(Guid = "603b30b2-6f12-42de-84b6-c772962e9d26")]
    public readonly InputSlot<float> OverrideTime = new();

    [Input(Guid = "6ebc3788-d3d5-44df-96d7-b88689f9e166", MappedType = typeof(AnimMath.Shapes))]
    public readonly InputSlot<int> Shape = new(); 
        
    [Input(Guid = "8923F351-7F6B-46F1-8DF6-9559534278BE")]
    public readonly InputSlot<Vector2> Rates = new();

    [Input(Guid = "97530728-a2a8-4d29-8ea4-e2170be70f18")]
    public readonly InputSlot<float> RateFactor = new();

    [Input(Guid = "62165CC4-9DA8-47DC-89AE-8B6CDE8DDA49")]
    public readonly InputSlot<Vector2> Phases = new();

    [Input(Guid = "140CBA08-E712-4C2B-A625-F270F1B72B54")]
    public readonly InputSlot<Vector2> Amplitudes = new();

    [Input(Guid = "D1FCDD1F-763B-4D25-9AB2-9240508EC4F6")]
    public readonly InputSlot<float> AmplitudeFactor = new();
        
    [Input(Guid = "304124E6-1FA1-4F6B-86DE-EF7769CDE1F6")]
    public readonly InputSlot<Vector2> Offsets = new();
        
    [Input(Guid = "7FD2EC56-05B3-4D19-8CC7-EB4144B7097D")]
    public readonly InputSlot<float> Bias = new();
        
    [Input(Guid = "74FECC5E-5CBC-4D0C-BC32-234B0F9C1547")]
    public readonly InputSlot<float> Ratio = new();
        
    [Input(Guid = "7a1f6dc7-2ae8-4cbb-9750-c17e460327d4", MappedType = typeof(AnimMath.SpeedFactors))]
    public readonly InputSlot<int> AllowSpeedFactor = new();

    #region operator Ui
    public double _normalizedTimeX;  
    public double _normalizedTimeY;  
    public AnimMath.Shapes _shape;   
    #endregion
}