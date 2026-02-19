using T3.Core.Utils;

namespace Lib.numbers.anim.animators;

[Guid("94a392e6-3e03-4ccf-a114-e6fafa263b4f")]
public sealed class SequenceAnim : Instance<SequenceAnim>
{
    [Output(Guid = "99f24d0d-bda5-44a5-afd1-cb144ba313e6", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    [Output(Guid = "1303BDF7-E547-44BB-84FC-1DE0DF50F1AF", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasStep = new();


    public SequenceAnim()
    {
        Result.UpdateAction += Update;
        WasStep.UpdateAction += Update;
    }
        
    // Only for custom UI
    public List<float> CurrentSequence = _emptySequence;
    public int CurrentSequenceIndex { get; private set; }
    public float NormalizedBarTime { get; private set; }
    public bool IsRecording { get; private set; }
        
    public void SetStepValue( int stepIndex, float normalizedValue)
    {
        if (_sequences.Count == 0)
        {
            return;
        }

        var charIndex = 0;
        var sequenceSearchIndex = 0;
        var stepSearchIndex = 0;
        while (charIndex < _sequencesDefinition.Length)
        {
            if (sequenceSearchIndex == CurrentSequenceIndex && stepSearchIndex == stepIndex)
            {
                var newStringBuilder = new StringBuilder(_sequencesDefinition);
                newStringBuilder[charIndex] = Convert.ToChar('0' + (int)(normalizedValue*9));
                _sequencesDefinition = newStringBuilder.ToString();
                Sequence.Value = _sequencesDefinition;
                    
                Sequence.TypedInputValue.Value = (_sequencesDefinition);
                Sequence.DirtyFlag.Invalidate();
                Sequence.Input.IsDefault = false;
                UpdateSequences();
                return;
            }

            var c = _sequencesDefinition[charIndex];
            if (c == '\n')
            {
                sequenceSearchIndex++;
                stepSearchIndex = 0;
            }
            else
            {
                stepSearchIndex++;
            }
            charIndex ++;
        }
    }

    private double _lastUpdateTime = 0;


        
    private void Update(EvaluationContext context)
    {
        // Detect time jump backwards (export/playback restart) and reset state
        if (context.LocalFxTime < _lastUpdateTime - 0.1)
        {
            _lastStepIndex = -1;  // Reset so first step triggers correctly
        }
        
        if (Math.Abs(context.LocalFxTime - _lastUpdateTime) < 0.0001f)
        {
            return;
        }

        _lastUpdateTime = context.LocalFxTime;
        
        
        _minValue = MinValue.GetValue(context);
        _maxValue = MaxValue.GetValue(context);
        _bias = Bias.GetValue(context);
        _rate = Rate.GetValue(context);
            
        var outputMode = (OutputModes)OutputMode.GetValue(context).Clamp(0, Enum.GetValues<OutputModes>().Length - 1);
            
            
        var hasIndexChanged = SequenceIndex.DirtyFlag.IsDirty;
        CurrentSequenceIndex = SequenceIndex.GetValue(context);

        var newSequences = Sequence.GetValue(context);

        if (newSequences != _sequencesDefinition || hasIndexChanged)
        {
            _sequencesDefinition = newSequences;
            UpdateSequences();
        }

        if (CurrentSequence.Count == 0)
        {
            Result.Value = 0;
            return;
        }

        var phase = Phase.GetValue(context);
        //var phaseShift = MathF.Abs(_rate) < 0.0001 ? 0 : phase / _rate;  
        
        var time = context.LocalFxTime * _rate + phase;
        if (OverrideTime.HasInputConnections)
        {
            var overrideTime = OverrideTime.GetValue(context);
            time = overrideTime * _rate;
        }
            
        NormalizedBarTime = (float)MathUtils.Fmod(time, 1).Clamp(0, 0.999999);

        var recValue = RecordValue.GetValue(context);
        var recordingMode = RecordingMode.GetEnumValue<RecordingModes2>(context);
            
        switch (recordingMode)
        {
            case RecordingModes2.None:
                break;
                
            case RecordingModes2.RecordValueChanges:
                if (_recordingStartTime > context.LocalFxTime)
                    _recordingStartTime = double.NegativeInfinity;
                    
                var timeSinceRecordStart = (context.LocalFxTime - _recordingStartTime);
                IsRecording = MathF.Abs(_rate) > 0.001f && timeSinceRecordStart < (1 / _rate) - (1f/CurrentSequence.Count)/_rate;
                var wasRecordTriggered = (Math.Abs(recValue - _lastRecordValue) > 0.0001f && recValue > _lastRecordValue);
                if (wasRecordTriggered)
                {
                    _lastRecordedIndex = _lastStepIndex;
                    SetStepValue(_lastStepIndex, recValue.Clamp(0, 1));
                    if (!IsRecording)
                    {
                        _recordingStartTime = context.LocalFxTime;
                    }
                }
                else if (IsRecording && _lastStepIndex != _lastRecordedIndex)
                {
                    SetStepValue(_lastStepIndex, 0);
                    _lastRecordedIndex = _lastStepIndex;
                }
                break;
                
            case RecordingModes2.RecordHitCycle:
                var hasIncreased = recValue - _lastRecordValue > 0.001f;
                var wasHit = MathUtils.WasTriggered(hasIncreased, ref _recordingValueIncreased);
                if (wasHit)
                {
                    // Clear all steps if it has been a while since the last record
                    var cyclesSinceLastRecord = (time - _recordingStartTime) / (_rate * 4);
                    if(cyclesSinceLastRecord > 1f)
                    {
                        for (var index = 0; index < CurrentSequence.Count; index++)
                        {
                            SetStepValue(index, 0);
                        }

                        _recordingStartTime = time;
                        _lastRecordedIndex = 0;
                    }
                        
                    var nearestStepIndex = (int)Math.Floor(NormalizedBarTime * CurrentSequence.Count);
                    SetStepValue(nearestStepIndex, 1);
                }

                break;
                
            case RecordingModes2.RecordHits:
                var shouldSetValue = _lastStepIndex != _lastRecordedIndex;
                var newValue = 0f;

                if (recValue - _lastRecordValue > 0.001f)
                {
                    shouldSetValue = true;    
                    newValue = recValue.Clamp(0,1);
                    _lastRecordedIndex = 0;
                }

                if (shouldSetValue)
                {
                    SetStepValue(_lastStepIndex, newValue);
                    _lastRecordedIndex = _lastStepIndex;
                }
                break;
                
            default:
                throw new ArgumentOutOfRangeException();
        }
            
        _lastRecordValue = recValue;
            
            
            
        var updateMode = (UpdateModes)UpdateMode.GetValue(context).Clamp(0, Enum.GetNames<UpdateModes>().Length - 1);
        switch (updateMode)
        {
            case UpdateModes.Random:
            {
                var seedValue = (uint)(time * CurrentSequence.Count);
                var randomValue = MathUtils.XxHash(seedValue);
                var fraction = ((NormalizedBarTime * CurrentSequence.Count) % 1 / CurrentSequence.Count).Clamp(0, 0.999999f); 
                NormalizedBarTime = ((float)(randomValue % CurrentSequence.Count)/CurrentSequence.Count + fraction ).Clamp(0, 0.999999f);
                Result.Value = NormalizedBarTime;
                break;
            }
            case UpdateModes.PingPong:
                var modTime = MathUtils.Fmod((time + 1) / 2, 1);
                NormalizedBarTime = (float)Math.Abs(2 * modTime - 1).Clamp(0, 0.999999f);
                break;
        }

        var stepIndex = (int)Math.Floor(NormalizedBarTime * CurrentSequence.Count);
            
        if (stepIndex < 0)
            stepIndex = 0;

        WasStep.Value = CurrentSequence.Count > 0 && stepIndex != _lastStepIndex && CurrentSequence[stepIndex] > 0;
        _lastStepIndex = stepIndex;

        var stepStrength = CurrentSequence[stepIndex];

        var stepTime = (float)(NormalizedBarTime * CurrentSequence.Count - stepIndex).Clamp(0, 1);
        var biasedTime = SchlickBias(stepTime, _bias);
        var stepBeat = (1 - biasedTime) * stepStrength;

        float returnValue = 0;
            
        switch (outputMode)
        {
            case OutputModes.Pulse:
                returnValue = MathUtils.Lerp(_minValue, _maxValue, stepBeat);
                break;
                
            case OutputModes.NormalizedValue:
                var normalizedStrength = stepStrength;
                returnValue = MathUtils.Lerp(_minValue, _maxValue,  SchlickBias( normalizedStrength, _bias) );
                break;
                
            case OutputModes.CharacterValue:
                returnValue = stepStrength * MaxCharacterValue;
                break;
        }
        Result.Value = returnValue;
            
        Result.DirtyFlag.Clear();
        WasStep.DirtyFlag.Clear();
    }

        
    private void UpdateSequences()
    {
        var sequences = _sequencesDefinition.Split("\n");
        _sequences.Clear();

        foreach (var s in sequences)
        {
            var minChar = (int)'0';
            var sequenceValues = new List<float>(s.Length);

            foreach (var c in s)
            {
                var v = c - minChar;
                sequenceValues.Add((v / MaxCharacterValue).Clamp(0, 1));
            }

            _sequences.Add(sequenceValues);
        }

        if (_sequences.Count == 0)
        {
            CurrentSequence = _emptySequence;
        }
        else
        {
            CurrentSequence = _sequences[CurrentSequenceIndex.Mod(_sequences.Count)];
        }
    }
        

    private static float SchlickBias(float x, float bias)
    {
        return x / ((1 / bias - 2) * (1 - x) + 1);
    }

    private readonly List<List<float>> _sequences = new(16);
    private static readonly List<float> _emptySequence = [];
    private string _sequencesDefinition = string.Empty;
    private const float MaxCharacterValue = 9;
    private bool _recordingValueIncreased;
        
    private double _recordingStartTime = double.NegativeInfinity;
    private int _lastRecordedIndex = 0;

    private int _lastStepIndex;
    private float _lastRecordValue;
    private float _minValue;
    private float _maxValue;
    private float _rate;
    private float _bias;

    private enum OutputModes
    {
        Pulse,
        NormalizedValue,
        CharacterValue,
    }
        
    private enum UpdateModes
    {
        Time,
        PingPong,
        Random,
    }
        
    private enum RecordingModes2
    {
        None,
        RecordHitCycle,
        RecordHits,
        RecordValueChanges,
    }

    [Input(Guid = "7BDFB9A8-87B3-4603-890C-FE755F4C4492")]
    public readonly InputSlot<string> Sequence = new();

    [Input(Guid = "F8AFEE64-627D-48E4-B4EA-A15601A01AA1")]
    public readonly InputSlot<int> SequenceIndex = new();

    [Input(Guid = "E3AD4E64-F7CF-49A1-B3AE-4D285C05103A", MappedType = typeof(UpdateModes))]
    public readonly InputSlot<int> UpdateMode = new();
        
    [Input(Guid = "F0AE47AE-5849-4D81-BAE0-9B6EC44949EF")]
    public readonly InputSlot<float> Rate = new();

    [Input(Guid = "3BC4BEEC-A11F-4F89-A38F-16FD59F5F605")]
    public readonly InputSlot<float> Phase = new();
    
    [Input(Guid = "5B86B2EC-1043-4A90-9A45-F629CFB7F713", MappedType = typeof(OutputModes))]
    public readonly InputSlot<int> OutputMode = new();

    [Input(Guid = "3AD1C6AB-C91A-4261-AEA5-29394D2926DF")]
    public readonly InputSlot<float> MinValue = new();

    [Input(Guid = "C5FFCF77-14FE-4A59-B41A-26579EF39326")]
    public readonly InputSlot<float> MaxValue = new();

    [Input(Guid = "1c9afc39-7bab-4042-86eb-c7e30595af8e")]
    public readonly InputSlot<float> Bias = new();

    [Input(Guid = "E45C6ABF-DF32-47CF-833D-E1391749B8F4", MappedType = typeof(RecordingModes2))]
    public readonly InputSlot<int> RecordingMode = new();
        
    [Input(Guid = "3CE54CA0-CD50-4DC2-BD3D-51E26EBF05CE")]
    public readonly InputSlot<float> RecordValue = new();
        
    [Input(Guid = "0E2FB821-460A-4147-99C0-5F6C696E7847")]
    public readonly InputSlot<float> OverrideTime = new();

        
}