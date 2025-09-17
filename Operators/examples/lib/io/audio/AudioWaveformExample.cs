namespace Examples.Lib.io.audio{
    [Guid("a9a768dd-d31e-471b-a6df-b088a37d1afb")]
    internal sealed class AudioWaveformExample : Instance<AudioWaveformExample>
    {

        [Output(Guid = "e06de265-199e-465f-89df-d6c387e7a5f4")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

