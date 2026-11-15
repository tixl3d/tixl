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

        // [RepeatFrame] subframes advance by their share of the frame; further cube faces and other repeats don't.
        var stepSec = context.RenderPass.GetStepDurationSec(Playback.LastFrameDuration);
        if (stepSec <= 0)
            return;

        _lastEvalTime = currentTime;

        // Calculate spring movement
        // based on https://x.com/itsmatharoo/status/1148297551931572224
        var targetValue = inputValue ;

        // Tuned as one step per 60 Hz frame; split into steps of at most one such frame so other rates and subframes match.
        var framesAt60 = (float)(stepSec * 60);
        var stepCount = Math.Max(1, (int)MathF.Ceiling(framesAt60));
        var stepFrames = framesAt60 / stepCount;
        var stepTension = tension is >= 0 and <= 1 ? 1 - MathF.Pow(1 - tension, stepFrames) : tension;
        for (var stepIndex = 0; stepIndex < stepCount; stepIndex++)
        {
            _springedValue = MathUtils.Lerp(_springedValue, (targetValue - Result.Value) * test, stepTension);
            Result.Value += _springedValue * stepFrames;
        }
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