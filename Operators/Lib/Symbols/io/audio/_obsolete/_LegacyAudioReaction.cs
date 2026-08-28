using T3.Operators.Utils;

namespace Lib.io.audio._obsolete;

[Guid("f8aed421-5e0e-4d1f-993c-1801153ebba8")]
internal sealed class _LegacyAudioReaction : Instance<_LegacyAudioReaction>
{
    [Output(Guid = "2aa4d0cb-c49d-41ce-aa74-794cc8682590", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Level = new();

    [Output(Guid = "E1D7D3AF-FFD7-460F-B861-F0A11EE287B0", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> PeakCount = new();

    [Output(Guid = "0CD3D908-C7C4-41D3-BEF2-C48A29B9842A", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> PeakDetected = new();

    public _LegacyAudioReaction()
    {
        Level.UpdateAction += Update;
        PeakCount.UpdateAction += Update;
        PeakDetected.UpdateAction += Update;
    }

    private double _lastEvalTime = 0;

    private void Update(EvaluationContext context)
    {
        var band = (FrequencyBands)Band.GetValue(context);
        var mode = (Modes)Mode.GetValue(context);
        var decay = Decay.GetValue(context);
        var modulo = UseModulo.GetValue(context);

        var t = context.LocalFxTime;
        if (Math.Abs(t - _lastEvalTime) < 0.001f) 
            return;

        _lastEvalTime = t;

        //var a = _SetAudioAnalysis.AudioAnalysisResult;

        var results = band == FrequencyBands.Bass
                          ? AudioAnalysisResult.Bass
                          : AudioAnalysisResult.HiHats;

        var peakDetected = results.PeakCount > _lastPeakCount;
            
        var usingModulo = modulo > 0;
        var isModuloPeak = peakDetected && (!usingModulo || results.PeakCount % modulo == 0);

        if (peakDetected && isModuloPeak)
        {
            _lastModuloPeakTime = context.Playback.FxTimeInBars;
        }
        var timeSinceModuloPeak = (float)(context.Playback.FxTimeInBars - _lastModuloPeakTime);
        var timeSincePeak = usingModulo
                                ? timeSinceModuloPeak
                                : results.TimeSincePeak;
            
        float value = 0;
        switch (mode)
        {
            case Modes.TimeSincePeak:
                    
                value = timeSincePeak;
                break;

            case Modes.Count:
                value = usingModulo
                            ? results.PeakCount / modulo
                            : results.PeakCount;
                break;

            case Modes.Peaks:
                value = (float)Math.Max(0, 1 - timeSincePeak * decay);
                break;

            case Modes.PeaksDecaying:
                value = (float)Math.Pow(decay + 1, -timeSincePeak);
                break;

            case Modes.Level:
                value = (float)results.AccumulatedEnergy;
                break;

            case Modes.MovingSum:
                if (double.IsNaN(_movingSum))
                    _movingSum = 0;
                    
                var step =Math.Pow(results.AccumulatedEnergy, decay);
                if (!double.IsNaN(step))
                    _movingSum += step;

                value = (float)(_movingSum % 10000);
                break;

            case Modes.RandomValue:
                if (isModuloPeak)
                {
                    _lastRandomValue = (float)_random.NextDouble();
                }
                value = _lastRandomValue;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        Level.Value = value * Amplitude.GetValue(context);

        PeakCount.Value = results.PeakCount;

        PeakDetected.Value = isModuloPeak;
        _lastPeakCount = results.PeakCount;

        Level.DirtyFlag.Clear();
        PeakCount.DirtyFlag.Clear();
        PeakDetected.DirtyFlag.Clear();
    }

    private double _lastModuloPeakTime;
    private float _lastRandomValue;

    private enum FrequencyBands
    {
        Bass,
        Highs,
    }

    private enum Modes
    {
        TimeSincePeak,
        Peaks,
        PeaksDecaying,
        Level,
        Count,
        RandomValue,
        MovingSum,
    }

    private int _lastPeakCount;
    private double _movingSum = 0;
    private Random _random = new();

    [Input(Guid = "15F841F5-5153-4383-90B9-F6A4F72D5D6B", MappedType = typeof(FrequencyBands))]
    public readonly InputSlot<int> Band = new();

    [Input(Guid = "D9069774-188B-4A5E-976A-053D0C893503", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "EC0FE09B-B925-4B14-8186-8C32B65AF9BB")]
    public readonly InputSlot<float> Amplitude = new();

    [Input(Guid = "E7FAC507-AD85-48F4-89D3-76600FF519EC")]
    public readonly InputSlot<float> Decay = new();

    [Input(Guid = "0E04D1F8-3BBD-46B1-BDE6-46BCDFDBE3AA")]
    public readonly InputSlot<int> UseModulo = new();
}