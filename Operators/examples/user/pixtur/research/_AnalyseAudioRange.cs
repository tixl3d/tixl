namespace Examples.user.pixtur.research;

[Guid("6bb3fc41-f1d4-467e-afc6-62b452ec36be")]
public class _AnalyseAudioRange : Instance<_AnalyseAudioRange>
{
    [Output(Guid = "3cdc4645-2094-4e36-b06c-fa0ccf5ac890")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "7181f674-8998-420b-a98b-094d22fdf174")]
    public readonly Slot<float> PulseLevel = new();

    [Output(Guid = "0b411b96-3907-45b3-9b2b-67f3b57668e5")]
    public readonly Slot<float> PeakCount = new();

    [Output(Guid = "a85d0955-a381-44f3-88e4-c2833c6219b2")]
    public readonly Slot<float> MovingSum = new();

    [Output(Guid = "a0b5f6b4-b21f-4745-9de2-ef706b3c1147")]
    public readonly Slot<float> TimeSincePeak = new();

    [Input(Guid = "b9befe9e-c11a-41c8-a127-410bd1681add")]
    public readonly InputSlot<System.Collections.Generic.List<float>> FFT = new();

    [Input(Guid = "42412b35-6942-4802-8f12-b83bba66c830")]
    public readonly InputSlot<float> RangeMin = new();

    [Input(Guid = "2d64bab8-3560-4b2a-9dfa-643a1a4ba6f2")]
    public readonly InputSlot<float> RangeMax = new();

    [Input(Guid = "e3582ee9-ce77-4249-bbdd-21fb9196b44f")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "f9004ee2-adc6-4d61-97c5-29c47b95281f")]
    public readonly InputSlot<float> MinTimeBetweenPeaks = new();

    [Input(Guid = "0cc060fb-b721-4ef1-936f-2a3aa8efd1f7")]
    public readonly InputSlot<bool> Reset = new();

}