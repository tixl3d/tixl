using T3.Core.Utils;

namespace Lib.numbers.floats.process;

[Guid("60adca24-4ae4-474a-b205-a814d89a7d53")]
internal sealed class AnalyzePianoMidi :Instance<AnalyzePianoMidi>{
    
    [Output(Guid = "DF5E7F93-7D89-4D0C-B3CC-18BF0E7655D2", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> VelocityPulse = new();

    [Output(Guid = "33C3A89B-6D62-4754-8C07-86B4A8FFC748", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> LastNormalizedNote = new();
    
    public AnalyzePianoMidi()
    {
        VelocityPulse.UpdateAction += Update;
        LastNormalizedNote.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var amplitude = Amplitude.GetValue(context);
        var gainAndBias = GainAndBias.GetValue(context);
        
        var dt = context.LocalFxTime - _lastUpdateTime;
        _lastUpdateTime = context.LocalFxTime;
        if (dt <= 0.0001)
            return;

        _peak *= PeakDecay.GetValue(context);
        
        var list = Input.GetValue(context);
        if (list == null || list.Count == 0)
        {
            VelocityPulse.Value = _peak;
            return;
        }

        if (list.Count != _lastValues.Count)
        {
            _lastValues.Clear();
            for (var i = 0; i < list.Count; i++)
            {
                _lastValues.Add(0);
            }
        }


        var maxVelocity = 0f;
        var maxVelocityIndex = -1;
        
        for (var i = 0; i < list.Count; i++)
        {
            var newValue = list[i];
            var lastValue = _lastValues[i];

            var v = ((newValue - lastValue)/127).ApplyGainAndBias(gainAndBias.X, gainAndBias.Y);
            
            if (v > 0)
            {
                _peak += v;
            }

            if (v > maxVelocity)
            {
                maxVelocity = v;
                maxVelocityIndex = i;
            }

            _lastValues[i] = newValue;
        }

        if (maxVelocityIndex != -1)
        {
            if (list.Count <= 1)
            {
                LastNormalizedNote.Value = 0.5f;
            }
            else
            {
                LastNormalizedNote.Value = (float) maxVelocityIndex / (list.Count - 1);
            }
        }

        VelocityPulse.Value = _peak * amplitude;

        VelocityPulse.DirtyFlag.Clear();
        LastNormalizedNote.DirtyFlag.Clear();
    }

    private readonly List<float> _lastValues = new(108);
    private double _lastUpdateTime;
    private float _peak;

    [Input(Guid = "3e02c9d5-28bb-4913-b983-bc5464c9f387")]
    public readonly InputSlot<List<float>> Input = new(new List<float>(20));
    
    [Input(Guid = "DBD07421-6E96-467A-91AE-C526316E6AEE")]
    public readonly InputSlot<float> PeakDecay = new(0);
    
    [Input(Guid = "78A357B0-668A-47D6-91DB-B172955E8436")]
    public readonly InputSlot<float> Amplitude = new(0);

    [Input(Guid = "B5761D3F-4C56-47C7-A2D5-06EC7BAA7D57")]
    public readonly InputSlot<Vector2> GainAndBias = new();

}