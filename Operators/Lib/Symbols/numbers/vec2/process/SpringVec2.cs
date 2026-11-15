using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.vec2.process;

[Guid("4ee040a3-e5dd-42f0-8f7e-0275d7c72538")]
internal sealed class SpringVec2 : Instance<SpringVec2>
{
    [Output(Guid = "de2a9765-9274-4b8c-bb74-0bc8bee673a8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector2> Result = new();

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    public SpringVec2()
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


    private Vector2 _springedValue;
    private double _lastEvalTime;

    [Input(Guid = "5ec00ae8-1cd5-418f-bd9f-1b925df08466")]
    public readonly InputSlot<Vector2> Value = new ();

    [Input(Guid = "9c8180fd-3e62-4fed-ad9a-c5c5b284e5e9")]
    public readonly InputSlot<float> Tension = new ();

    [Input(Guid = "54d7918f-1bca-4198-a12d-0bb9df9e3285")]
    public readonly InputSlot<float> Strength = new ();

    [Input(Guid = "1bd48756-f739-42b2-a1f3-bb5181a28392")]
    public readonly InputSlot<bool> UseAppRunTime = new ();

}