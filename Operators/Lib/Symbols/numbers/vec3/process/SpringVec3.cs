using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.vec3.process;

[Guid("25cc43ed-f6a3-4f3d-a87d-5204f4a5bfc2")]
internal sealed class SpringVec3 : Instance<SpringVec3>
{
    [Output(Guid = "abc92693-3797-4fdd-8377-17aeaf1edf86", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Result = new();

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    public SpringVec3()
    {
        Result.UpdateAction = Update;
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


    private Vector3 _springedValue;
    private double _lastEvalTime;

    [Input(Guid = "5afee6d8-0697-43ab-9005-7be99cf928b6")]
    public readonly InputSlot<Vector3> Value = new ();

    [Input(Guid = "211d1e8c-594c-4265-8846-e08e4f1c7743")]
    public readonly InputSlot<float> Tension = new ();

    [Input(Guid = "df280e09-04fd-4ff2-a868-4840a3b6386c")]
    public readonly InputSlot<float> Strength = new ();

    [Input(Guid = "74013738-aec5-4d4f-a5c7-7f1db8e3169c")]
    public readonly InputSlot<bool> UseAppRunTime = new ();

}