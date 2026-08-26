using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.@float.process;

[Guid("0b337922-aeca-473a-bfe9-4ab6ff804b11")]
internal sealed class Spring : Instance<Spring>
{
    [Output(Guid = "e989b1ae-c7c0-4209-89c7-3aa589695d85", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    public Spring()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var inputValue = Value.GetValue(context);
        var tension = Tension.GetValue(context);
        var test = Strength.GetValue(context);

        var currentTime = UseAppRunTime.GetValue(context) ? Playback.RunTimeInSecs : context.LocalFxTime;
        if (Math.Abs(currentTime - _lastEvalTime) < MinTimeElapsedBeforeEvaluation)
            return;

        if (context.IntVariables.TryGetValue("__MotionBlurPass", out var motionBlurPass))
        {
            if (motionBlurPass > 0)
            {
                //Log.Debug($"Skip motion blur pass {motionBlurPass}");
                return;
            }
        }

        _lastEvalTime = currentTime;

        // Calculate spring movement
        // based on https://x.com/itsmatharoo/status/1148297551931572224
        var targetValue = inputValue ;

        _springedValue = MathUtils.Lerp(_springedValue, (targetValue - Result.Value) * test, tension);
        Result.Value += _springedValue;
    }


    private float _springedValue;
    private double _lastEvalTime;

    [Input(Guid = "782eb1a4-ee2a-4326-a7dc-7a40c5fb9b7c")]
    public readonly InputSlot<float> Value = new InputSlot<float>();

    [Input(Guid = "8cd61153-9e93-42bf-9fb6-ad29988e780f")]
    public readonly InputSlot<float> Tension = new InputSlot<float>();

    [Input(Guid = "7b130212-e567-4fd3-89aa-0b833c2ae490")]
    public readonly InputSlot<float> Strength = new InputSlot<float>();

    [Input(Guid = "bafae88a-4000-442f-9698-7d4d50e3eccf")]
    public readonly InputSlot<bool> UseAppRunTime = new InputSlot<bool>();

}