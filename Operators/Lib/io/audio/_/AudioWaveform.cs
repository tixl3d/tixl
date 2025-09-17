using T3.Core.Audio;

namespace Lib.io.audio._;

[Guid("55E338F7-6D8C-4B61-8E10-9BB4D5FCFF91")]
internal sealed class AudioWaveform : Instance<AudioWaveform>
{
    [Output(Guid = "AB0976EB-2B14-4AA2-A12C-398310A6E07B", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Left = new();

    [Output(Guid = "71310214-FD15-49EE-9C25-6200AA1E8566", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Right = new();

    [Output(Guid = "F01CFAFC-F0C0-429B-94BD-A9C423A669C1", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Low = new();

    [Output(Guid = "4EC238E7-86B0-4400-BCB9-A10E3C62139E", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Mid = new();
    
    [Output(Guid = "6BBCA7C1-822F-41E2-B821-BEA066CB5AF3", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> High = new();
    
    public AudioWaveform()
    {
        Left.UpdateAction = Update;
        Right.UpdateAction = Update;
        Low.UpdateAction = Update;
        Mid.UpdateAction = Update;
        High.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        WaveFormProcessing.UpdateWaveformData();
        
        Left.Value  =  WaveFormProcessing.WaveformLeftBuffer.ToList();
        Right.Value = WaveFormProcessing.WaveformRightBuffer.ToList();
        Low.Value = WaveFormProcessing.WaveformLowBuffer.ToList();
        Mid.Value = WaveFormProcessing.WaveformMidBuffer.ToList();
        High.Value   = WaveFormProcessing.WaveformHighBuffer.ToList();
    }
}