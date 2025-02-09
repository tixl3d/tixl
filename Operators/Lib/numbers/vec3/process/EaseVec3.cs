using System;
using System.Numerics;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using static T3.Core.Animation.VDefinition;
using static T3.Core.Utils.EasingFunctions;

namespace T3.Operators.Types.Id_12dccab1_1d7d_4005_a4c1_0bebeeaeb6d3
{
    public class EaseVec3 : Instance<EaseVec3>
    {
        [Output(Guid = "b54d5398-3671-44b7-961f-0fb092a2c78b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Vector3> Result = new();

        private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

        public EaseVec3()
        {
            Result.UpdateAction = Update;
        }

        private void Update(EvaluationContext context)
        {
            var inputValue = Value.GetValue(context);  // Target value (0 or 1)
            var duration = Duration.GetValue(context); // Duration of the animation in seconds
            var easeMode = Interpolation.GetValue(context);     // Easing function selector
            var easeDirection = (EaseDirection)Mode.GetValue(context);


            var currentTime = UseAppRunTime.GetValue(context) ? Playback.RunTimeInSecs : context.LocalFxTime;
            if (Math.Abs(currentTime - _lastEvalTime) < MinTimeElapsedBeforeEvaluation)
                return;

            if (context.IntVariables.TryGetValue("__MotionBlurPass", out var motionBlurPass) && motionBlurPass > 0)
            {
                return;
            }

            _lastEvalTime = currentTime;
           
            if (Vector3.Distance(inputValue, _previousInputValue) > 0.001f)
            {
                _startTime = currentTime;
                _initialValue = Result.Value;
                _targetValue = inputValue;
            }


            // Calculate progress based on elapsed time and duration
            var elapsedTime = (float)(currentTime - _startTime);
            var progress = Math.Clamp(elapsedTime / duration, 0f, 1f);

            // Apply selected easing function based on easeMode
            //var easedProgress = progress;
            var easedProgress = progress;
            switch (easeDirection)
            {
                case EaseDirection.In:
                    easedProgress = easeMode switch
                    {
                        1 => EasingFunctions.InSine(progress),
                        2 => EasingFunctions.InQuad(progress),
                        3 => EasingFunctions.InCubic(progress),
                        4 => EasingFunctions.InQuart(progress),
                        5 => EasingFunctions.InQuint(progress),
                        6 => EasingFunctions.InExpo(progress),
                        7 => EasingFunctions.InCirc(progress),
                        8 => EasingFunctions.InBack(progress),
                        9 => EasingFunctions.InElastic(progress),
                        10 => EasingFunctions.InBounce(progress),
                        _ => progress
                    };
                    break;

                case EaseDirection.Out:
                    easedProgress = easeMode switch
                    {
                        1 => EasingFunctions.OutSine(progress),
                        2 => EasingFunctions.OutQuad(progress),
                        3 => EasingFunctions.OutCubic(progress),
                        4 => EasingFunctions.OutQuart(progress),
                        5 => EasingFunctions.OutQuint(progress),
                        6 => EasingFunctions.OutExpo(progress),
                        7 => EasingFunctions.OutCirc(progress),
                        8 => EasingFunctions.OutBack(progress),
                        9 => EasingFunctions.OutElastic(progress),
                        10 => EasingFunctions.OutBounce(progress),
                        _ => progress
                    };
                    break;

                case EaseDirection.InOut:
                    easedProgress = easeMode switch
                    {
                        1 => EasingFunctions.InOutSine(progress),
                        2 => EasingFunctions.InOutQuad(progress),
                        3 => EasingFunctions.InOutCubic(progress),
                        4 => EasingFunctions.InOutQuart(progress),
                        5 => EasingFunctions.InOutQuint(progress),
                        6 => EasingFunctions.InOutExpo(progress),
                        7 => EasingFunctions.InOutCirc(progress),
                        8 => EasingFunctions.InOutBack(progress),
                        9 => EasingFunctions.InOutElastic(progress),
                        10 => EasingFunctions.InOutBounce(progress),
                        _ => progress
                    };
                    break;
            };
            Result.Value = new Vector3(
                            MathUtils.Lerp(_initialValue.X, _targetValue.X, easedProgress),
                            MathUtils.Lerp(_initialValue.Y, _targetValue.Y, easedProgress),
                            MathUtils.Lerp(_initialValue.Z, _targetValue.Z, easedProgress)
                );
            

            _previousInputValue = inputValue;
        }
     

        

        private double _lastEvalTime;
        private double _startTime;
        private Vector3 _initialValue;
        private Vector3 _targetValue;
        private Vector3 _previousInputValue;

        [Input(Guid = "16e48eb8-3baf-4e0b-a75e-72a747c9fead")]
        public readonly InputSlot<System.Numerics.Vector3> Value = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "c7ebf4ed-5477-4461-ac68-8a7ac9849ce6")]
        public readonly InputSlot<float> Duration = new InputSlot<float>();

        [Input(Guid = "2a450207-489e-4a8d-83ab-37ed8167eebe")]
        public readonly InputSlot<bool> UseAppRunTime = new InputSlot<bool>();

        [Input(Guid = "1867ff18-f510-4c84-a3be-a4123e878133", MappedType = typeof(EaseDirection))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();

        [Input(Guid = "9ef45ff9-9d59-44b7-b03d-37a1be40a776", MappedType = typeof(EasingFunctions.EasingType))]
        public readonly InputSlot<int> Interpolation = new InputSlot<int>();

        


        
    }
}
