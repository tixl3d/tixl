namespace Lib.numbers.@float.adjust;

[Guid("ae0c87d6-2b1e-4e28-b0d0-8611a2f7e152")]
internal sealed class Round : Instance<Round>
{
    [Output(Guid = "a886fa83-a8cc-4022-b4b1-26c134095223")]
    public readonly Slot<float> Result = new();

    public Round()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var steps = StepsPerUnit.GetValue(context);
        var ratio = RoundRatio.GetValue(context);
            
        Result.Value = RoundValue2(v, steps,ratio);
    }
        
    public static float RoundValue2(float i, float stepsPerUnit, float stepRatio)
    {
        float u = 1 / stepsPerUnit;
        float v = stepRatio / (2 * stepsPerUnit);
        float m = i % u;
        float r = m - (m < v
                           ? 0
                           : m > u - v
                               ? u
                               : (m - v) / (1 - 2 * stepsPerUnit * v));
        float y = i - r;
        return y;
    }
        
        
    [Input(Guid = "e911807f-b3a7-44e9-82d2-f04608b39ec3")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "31a045cf-9892-4d1b-b961-73bf73e58b6a")]
    public readonly InputSlot<float> StepsPerUnit = new();
        
    [Input(Guid = "B2D2D730-ED57-4B5F-AA34-2060D86EFB47")]
    public readonly InputSlot<float> RoundRatio = new();
}