using T3.Core.Audio;
using T3.Core.Utils;

namespace Lib.io.audio._;

[Guid("b72d968b-0045-408d-a2f9-5c739c692a66")]
internal sealed class AudioFrequencies : Instance<AudioFrequencies>
{
    [Output(Guid = "B3EFDF25-4692-456D-AA48-563CFB0B9DEB", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> FftBuffer = new();

    public AudioFrequencies()
    {
        FftBuffer.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // if (!string.IsNullOrEmpty(Playback.Current.Settings?.AudioInputDeviceName) 
        //     && !WasapiAudioInput.DevicesInitialized)
        // {
        //     WasapiAudioInput.Initialize(context.Playback.Settings);
        // }
            
        var mode = (Modes)Mode.GetValue(context).Clamp(0, Enum.GetNames(typeof(Modes)).Length-1);
        switch (mode)
        {
            case Modes.RawFft:
                FftBuffer.Value = AudioAnalysis.FftGainBuffer == null
                                      ? _emptyList
                                      : AudioAnalysis.FftGainBuffer.ToList();
                    
                break;

            case Modes.NormalizedFft:
                FftBuffer.Value = AudioAnalysis.FftNormalizedBuffer == null
                                      ? _emptyList
                                      : AudioAnalysis.FftNormalizedBuffer.ToList();
                break;
                
            case Modes.FrequencyBands:
                FftBuffer.Value = AudioAnalysis.FrequencyBands == null
                                      ? _emptyList
                                      : AudioAnalysis.FrequencyBands.ToList();
                break;

            case Modes.FrequencyBandsPeaks:
                FftBuffer.Value = AudioAnalysis.FrequencyBandPeaks == null
                                      ? _emptyList
                                      : AudioAnalysis.FrequencyBandPeaks.ToList();

                break;
                
            case Modes.FrequencyBandsAttacks:
                FftBuffer.Value = AudioAnalysis.FrequencyBandAttacks == null
                                      ? _emptyList
                                      : AudioAnalysis.FrequencyBandAttacks.ToList();

                break;
                
        }
            
    }

    private enum Modes
    {
        RawFft,
        NormalizedFft,
        FrequencyBands,
        FrequencyBandsPeaks,
        FrequencyBandsAttacks,
    }
        
    [Input(Guid = "02A09286-19C9-4CEE-9439-260701F6DE58", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

        
    private static readonly List<float> _emptyList = new();
}