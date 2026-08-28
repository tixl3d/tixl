using T3.Core.Animation;
using T3.Core.Utils;
using static T3.Core.Utils.EasingFunctions;

namespace Lib.numbers.@vec2.process;
[Guid("93d9b8f4-d92f-4727-8d06-c1add3a74fe7")]
internal sealed class EaseVec2 : Instance<EaseVec2>
{
    [Output(Guid = "8e972216-4a0b-47a3-856a-37a3861e2017", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector2> Result = new();

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    public EaseVec2()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var inputValue = Value.GetValue(context);  
        var duration = Duration.GetValue(context); 
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
        if (Vector2.Distance(inputValue, _previousInputValue) > 0.001f)
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
    private Vector2 _initialValue;
    private Vector2 _targetValue;
    private Vector2 _previousInputValue;

    [Input(Guid = "3156deda-67ba-42eb-9418-7f28ea4b4ca5")]
    public readonly InputSlot<Vector2> Value = new();

    [Input(Guid = "51989ff4-a9c6-4fbc-bbc1-79b7af859e23")]
    public readonly InputSlot<float> Duration = new();

    [Input(Guid = "38f566a4-d41f-43d7-9ee5-1be3cb20e21c")]
    public readonly InputSlot<bool> UseAppRunTime = new();

    [Input(Guid = "5f2c66dd-dfaf-4aa0-b184-01e89819b317", MappedType = typeof(EaseDirection))]
    public readonly InputSlot<int> Direction = new();

    [Input(Guid = "19da524f-6cea-4da0-8ee2-9b47ef8a93a7", MappedType = typeof(Interpolations))]
    public readonly InputSlot<int> Interpolation = new();

}