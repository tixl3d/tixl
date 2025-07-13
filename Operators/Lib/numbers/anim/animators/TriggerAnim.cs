using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.anim.animators;

[Guid("95d586a2-ee14-4ff5-a5bb-40c497efde95")]
public sealed class TriggerAnim : Instance<TriggerAnim>
{
    [Output(Guid = "aac4ecbf-436a-4414-94c1-53d517a8e587", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    [Output(Guid = "863C0A57-E893-4536-9EFE-6D001CB9D999", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasCompleted = new();
        
    public TriggerAnim()
    {
        Result.UpdateAction += Update;
        HasCompleted.UpdateAction += Update;
    }

    private enum Directions
    {
        Forward,
        Backwards,
        None
    }

    private void Update(EvaluationContext context)
    {
        // Prevent double evaluation to so all connected outputs to WasHit receive event.
        if (Math.Abs(context.LocalFxTime - _lastUpdateTime) < double.Epsilon)
            return;

        _lastUpdateTime = context.LocalFxTime;
        
        _baseValue = Base.GetValue(context);
        _amplitudeValue = Amplitude.GetValue(context);
        _bias = Bias.GetValue(context);
        _shape = Shape.GetEnumValue<Shapes>(context);
        _duration = Duration.GetValue(context);
        _delay = Delay.GetValue(context);

        var timeMode = TimeMode.GetEnumValue<Times>(context);
        var currentTime = timeMode switch
                              {
                                  Times.PlayTime   => context.Playback.TimeInBars,
                                  Times.AppRunTime => Playback.RunTimeInSecs,
                                  _                => context.LocalFxTime
                              };

        var animMode = AnimMode.GetEnumValue<AnimModes>(context);
        var triggerVariableName = UseTriggerVar.GetValue(context);
            
        var isTriggeredByVar = !Trigger.HasInputConnections 
                               && context.IntVariables.GetValueOrDefault(triggerVariableName, 0 ) == 1;

        var triggered = Trigger.GetValue(context) || isTriggeredByVar;
        if (triggered != _trigger)
        {
            HasCompleted.Value = false;
            _trigger = triggered;

            if (animMode == AnimModes.ForwardAndBackwards)
            {
                _triggerTime = currentTime;
                _currentDirection = triggered ? Directions.Forward : Directions.Backwards;
                _startProgress = LastFraction;
            }
            else
            {
                if (triggered)
                {
                    if (animMode == AnimModes.OnlyOnTrue)
                    {
                        _triggerTime = currentTime;
                        _currentDirection = Directions.Forward;
                        LastFraction = 0;
                    }
                }
                else
                {
                    if (animMode == AnimModes.OnlyOnFalse)
                    {
                        _triggerTime = currentTime;
                        _currentDirection = Directions.Backwards;
                        LastFraction = 1;
                    }
                }
            }
        }


        if (animMode == AnimModes.ForwardAndBackwards)
        {
            var dp = (float)((currentTime - _triggerTime) / _duration);
            switch (_currentDirection)
            {
                case Directions.Forward:
                {
                        var delayedDp = (currentTime - _triggerTime < _delay) ? 0 : (float)((currentTime - _triggerTime - _delay) / _duration);
                        LastFraction = _startProgress + delayedDp;
                        if (LastFraction >= 1)
                        {
                            HasCompleted.Value = true;
                            LastFraction = 1;
                            _currentDirection = Directions.None;
                        }
                        break;
                    }
                case Directions.Backwards:
                {
                    LastFraction = _startProgress - dp;
                    if (LastFraction <= 0)
                    {
                        LastFraction = 0;
                        _currentDirection = Directions.None;
                    }

                    break;
                }
            }
        }
        else
        {
            switch (_currentDirection)
            {
                case Directions.Forward:
                {
                        // Apply delay to the progress calculation
                        var timeSinceTrigger = currentTime - _triggerTime;
                        if (timeSinceTrigger < _delay)
                        {
                            LastFraction = 0;
                        }
                        else
                        {
                            LastFraction = (timeSinceTrigger - _delay) / _duration;
                            if (LastFraction >= 1)
                            {
                                LastFraction = 1;
                                HasCompleted.Value = true;
                                _currentDirection = Directions.None;
                            }
                        }
                        break;
                    }
                case Directions.Backwards:
                {
                    LastFraction =   1+( _triggerTime- currentTime )/_duration;
                    if (LastFraction < 0)
                    {
                        LastFraction = 0;
                        _currentDirection = Directions.None;
                    }

                    break;
                }
            }
        }
            
        var normalizedValue = CalcNormalizedValueForFraction(LastFraction, (int)_shape);
        if (double.IsNaN(LastFraction) || double.IsInfinity(LastFraction))
        {
            LastFraction = 0;
        }
            
        //Result.Value = MathUtils.Lerp(_baseValue, _amplitudeValue,  normalizedValue);
        Result.Value = _baseValue + _amplitudeValue *  normalizedValue;
    }
        
    public float CalcNormalizedValueForFraction(double t, int shapeIndex)
    {
        //var fraction = CalcFraction(t);
        var value = MapShapes[shapeIndex]((float)t);
        var biased = SchlickBias(value, _bias);
        return biased;
    }

    private float SchlickBias(float x, float bias)
    {
        return x / ((1 / bias - 2) * (1 - x) + 1);
    }


    private delegate float MappingFunction(float fraction);
    private static readonly MappingFunction[] MapShapes =
        {
            f => f.Clamp(0,1), // 0: Linear
            f => MathUtils.SmootherStep(0,1,f.Clamp(0,1)), //1: Smooth Step
            f => MathUtils.SmootherStep(0,1,f.Clamp(0,1)/2) *2   , //2: Easy In
            f => MathUtils.SmootherStep(0,1,f.Clamp(0,1)/2 + 0.5f) *2 -1, //3: Easy Out
            f => MathF.Sin(f.Clamp(0,1) * 40) * MathF.Pow(1-f.Clamp(0.0001f, 1),4) ,  //4: Shake
            f => f<=0 ? 0 : (1-f.Clamp(0,1)), // 5: Kick
        };
        
    private bool _trigger;
    private Shapes _shape;
    private float _bias;
    private float _baseValue;
    private float _amplitudeValue;
    private float _duration = 1;
    private float _delay;
    private double _lastUpdateTime = -1;

    private double _startProgress;
    public double LastFraction;
        
    public enum Shapes
    {
        Linear = 0,
        SmoothStep = 1,
        EaseIn = 2,
        EaseOut = 3,
        Shake = 4,
        Kick = 5,
    }


    public enum AnimModes
    {
        OnlyOnTrue,
        OnlyOnFalse,
        ForwardAndBackwards,
    }
        
    private Directions _currentDirection = Directions.None;
    private double _triggerTime;

    [Input(Guid = "62949257-ADB3-4C67-AC0A-D37EE28DA81B")]
    public readonly InputSlot<bool> Trigger = new();

    [Input(Guid = "c0fa79d5-2c49-4d40-998f-4eb0101ae050", MappedType = typeof(Shapes))]
    public readonly InputSlot<int> Shape = new();
        
    [Input(Guid = "9913FCEA-3994-40D6-84A1-D56525B31C43", MappedType = typeof(AnimModes))]
    public readonly InputSlot<int> AnimMode = new();

    [Input(Guid = "0d56fc27-fa15-4f1e-aa09-f97af93d42c7")]
    public readonly InputSlot<float> Duration = new();
        
    [Input(Guid = "3AD8E756-7720-4F43-85DA-EFE1AF364CFE")]
    public readonly InputSlot<float> Base = new();
        
    [Input(Guid = "287fa06c-3e18-43f2-a4e1-0780c946dd84")]
    public readonly InputSlot<float> Amplitude = new();

    [Input(Guid = "214e244a-9e95-4292-81f5-cd0199f05c66")]
    public readonly InputSlot<float> Delay = new();

    [Input(Guid = "9bfd5ae3-9ca6-4f7b-b24b-f554ad4d0255")]
    public readonly InputSlot<float> Bias = new();
        
    [Input(Guid = "06E511D2-891A-4F81-B49E-327410A2CB95", MappedType = typeof(Times))]
    public readonly InputSlot<int> TimeMode = new();
        
    private enum Times
    {
        LocalFxTime,
        PlayTime,
        AppRunTime,
    }
        
    [Input(Guid = "FFECB0C3-4D62-40F6-8F46-B982AE0A1800")]
    public readonly InputSlot<string> UseTriggerVar = new();
}