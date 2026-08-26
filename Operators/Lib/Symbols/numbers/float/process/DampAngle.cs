using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.@float.process;

[Guid("9757618d-e72a-4507-8352-6f824b56cc58")]
internal sealed class DampAngle : Instance<DampAngle>
{
    [Output(Guid = "bdc667e1-2557-4f66-aeb3-d9deccb888f9")]
    public readonly Slot<float> Result = new();

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    public DampAngle()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var targetValue = Value.GetValue(context);
            
        var targetAngle = _dampedValue + DeltaAngle(_dampedValue, targetValue);

        var damping = Damping.GetValue(context);

        var currentTime = UseAppRunTime.GetValue(context) ? Playback.RunTimeInSecs : context.LocalFxTime;
        if (Math.Abs(currentTime - _lastEvalTime) < MinTimeElapsedBeforeEvaluation)
            return;

        _lastEvalTime = currentTime;

        var method = (DampFunctions.Methods)Method.GetValue(context).Clamp(0, 1);
        _dampedValue = DampFunctions.DampenFloat(targetAngle, _dampedValue, damping, ref _velocity, method);
            
        MathUtils.ApplyDefaultIfInvalid(ref _dampedValue, 0);
        MathUtils.ApplyDefaultIfInvalid(ref _velocity, 0);

        Result.Value = _dampedValue;
    }

    private static float Repeat(float t, float length) {
        return (t - MathF.Floor(t / length) * length).Clamp(0.0f, length);
    }

    private float DeltaAngle(float current, float target) 
    {
        var num = Repeat(target - current, 360f);
        if (num > 180f) 
            num -= 360f;
            
        return num;
    }

    private float _dampedValue;
    private float _velocity;
    private double _lastEvalTime;
        
    [Input(Guid = "49366f9d-0d85-4e87-ae54-6f048b1dc4b0")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "eedfea53-3e92-455a-ab40-5d7660d0a837")]
    public readonly InputSlot<float> Damping = new();
        
    [Input(Guid = "4b131e85-8c5f-410a-b91c-cfd87a613231")]
    public readonly InputSlot<int> Method = new();
        
    [Input(Guid = "0ff04a75-2f3b-4a80-80cb-e1c04cdadcfd")]
    public readonly InputSlot<bool> UseAppRunTime = new();

}