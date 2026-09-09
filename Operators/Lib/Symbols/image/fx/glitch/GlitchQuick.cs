namespace Lib.image.fx.glitch;

[Guid("53c8b5f4-fd3b-4a7c-a22a-dea37c45b6d4")]
internal sealed class GlitchQuick :Instance<GlitchQuick>{
    [Output(Guid = "1b4f3472-4542-4b22-96fb-970a60234799")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "5f073394-8bb6-4406-b25e-6556734c8284")]
    public readonly InputSlot<Texture2D> Texture2d = new();

        [Input(Guid = "2a28f084-bc2f-4458-8ad8-f3bf11086fc4")]
        public readonly InputSlot<bool> SortPixelEnabled = new InputSlot<bool>();

        [Input(Guid = "b7b21d3c-80e1-450e-a1c7-b8720b550924")]
        public readonly InputSlot<float> DisplacementIntensity = new InputSlot<float>();

        [Input(Guid = "80c6ed94-e5e5-480b-b69e-2c4f9b2935c7")]
        public readonly InputSlot<float> BlackFlashesProbability = new InputSlot<float>();

        [Input(Guid = "bcadf77c-be02-482d-9cd7-87085831e9cd")]
        public readonly InputSlot<float> RandomColorBlockSize = new InputSlot<float>();

}