namespace Examples.Lib.io.audio{
    [Guid("a9a768dd-d31e-471b-a6df-b088a37d1afb")]
    internal sealed class AudioWaveformExample : Instance<AudioWaveformExample>
    {

        [Output(Guid = "e06de265-199e-465f-89df-d6c387e7a5f4")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();

        [Input(Guid = "c6d013f2-1502-485f-b683-15832b5b204e")]
        public readonly InputSlot<bool> ShowInstructions = new InputSlot<bool>();


    }
}

