using T3.Core.Animation;
using T3.Core.Utils;
using static T3.Core.Utils.EasingFunctions;

namespace Lib.numbers.@float.process;

[Guid("6e29bd11-7927-4f74-a35e-a6b129464a55")]
internal sealed class Ease : Instance<Ease>
{
    [Output(Guid = "75ff4efa-7ca7-4989-bd9c-42302cb248ca", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    public Ease()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var inputValue = Value.GetValue(context);  // Target value (0 or 1)
        var duration = Duration.GetValue(context); // Duration of the animation in seconds
        if (duration == 0)
            duration = 0.0001f;

        var easeMode = Interpolation.GetEnumValue<Interpolations>(context);     // Easing function selector
        var easeDirection = Direction.GetEnumValue<EaseDirection>(context);


        var currentTime = UseAppRunTime.GetValue(context) ? Playback.RunTimeInSecs : context.LocalFxTime;
        if (Math.Abs(currentTime - _lastEvalTime) < MinTimeElapsedBeforeEvaluation)
            return;

        _lastEvalTime = currentTime;

        if (context.IntVariables.TryGetValue("__MotionBlurPass", out var motionBlurPass) && motionBlurPass > 0)
            return;

        // Check if input has changed to trigger new animation
        if (Math.Abs(inputValue - _previousInputValue) > 0.001f)
        {
            _startTime = currentTime;
            _initialValue = Result.Value;
            _targetValue = inputValue;
        }

        // Calculate progress based on elapsed time and duration
        var elapsedTime = (float)(currentTime - _startTime);
        var progress = (elapsedTime / duration).Clamp(0f, 1f);

        var easedProgress = EasingFunctions.ApplyEasing(progress, easeDirection, easeMode);
        
        Result.Value = MathUtils.Lerp(_initialValue, _targetValue, easedProgress);
        _previousInputValue = inputValue;
    }

    private double _lastEvalTime;
    private double _startTime;
    private float _initialValue;
    private float _targetValue;
    private float _previousInputValue;

    [Input(Guid = "c2107af4-7b4f-43ef-97fe-934833790032")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "da40ddcd-cef8-494a-a7f8-cd8ddfbd7603")]
    public readonly InputSlot<float> Duration = new();

    [Input(Guid = "5580eee8-db7b-4e9b-898d-5c7680b0c302")]
    public readonly InputSlot<bool> UseAppRunTime = new();

    [Input(Guid = "fe0231d9-03b6-466f-82e9-852b892adf2e", MappedType = typeof(EaseDirection))]
    public readonly InputSlot<int> Direction = new();

    [Input(Guid = "bc388ea3-e1e6-4773-95e5-b8a649c3344f", MappedType = typeof(Interpolations))]
    public readonly InputSlot<int> Interpolation = new();

}