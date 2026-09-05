using T3.Operators.Utils;

namespace Lib.io.audio._;

[Guid("ecbafbeb-c14b-4507-953f-80bc6676d077")]
internal sealed class _SetAudioAnalysis : Instance<_SetAudioAnalysis>
{
    [Output(Guid = "4a483388-f54e-4403-8ab8-6b75cc9ba7bf")]
    public readonly Slot<Command> Output = new();

    public _SetAudioAnalysis()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        AudioAnalysisResult.Bass.PeakCount = BeatCount.GetValue(context);
        AudioAnalysisResult.Bass.TimeSincePeak= TimeSinceBeat.GetValue(context);
        AudioAnalysisResult.Bass.AccumulatedEnergy= BeatSum.GetValue(context);
            
        AudioAnalysisResult.HiHats.PeakCount= HiHatCount.GetValue(context);
        AudioAnalysisResult.HiHats.TimeSincePeak= TimeSinceHiHat.GetValue(context);
        AudioAnalysisResult.HiHats.AccumulatedEnergy= AccumulatedHiHat.GetValue(context);
    }
        
        
    [Input(Guid = "546F31C1-EDB4-4EE9-A5E7-4C66687AE74A")]
    public readonly InputSlot<float> TimeSinceBeat = new();
        
    [Input(Guid = "A379E4F0-CFB6-4D90-BBD7-D12A0C7A2E21")]
    public readonly InputSlot<int> BeatCount = new();
        
    [Input(Guid = "F65EF8E7-92CF-4A93-922B-B746A550F554")]
    public readonly InputSlot<float> BeatSum = new();
        
        
    [Input(Guid = "04B13EB6-83ED-441E-8856-4DDCAD898324")]
    public readonly InputSlot<float> TimeSinceHiHat = new();
        
    [Input(Guid = "D5AC7D01-F402-4CAB-A25D-5F5488D537EA")]
    public readonly InputSlot<int> HiHatCount = new();
        
    [Input(Guid = "6D2973A9-0AFD-4131-B8B3-430ED9E63F72")]
    public readonly InputSlot<float> AccumulatedHiHat = new();
}