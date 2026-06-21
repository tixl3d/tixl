namespace Lib.image.fx.feedback;

[Guid("a2ea3af5-d78b-46b4-84db-09d31f042798")]
internal sealed class AdvancedFeedback2 : Instance<AdvancedFeedback2>
{
    [Output(Guid = "451bf2c8-8daf-403b-a064-c7252b03c74d")]
    public readonly Slot<Texture2D> ColorBuffer = new();

        [Input(Guid = "36e16cc0-3c9e-4615-9ac0-2d2f07150deb")]
        public readonly MultiInputSlot<T3.Core.DataTypes.Command> Command = new MultiInputSlot<T3.Core.DataTypes.Command>();

        [Input(Guid = "73ba7691-784f-4706-8a88-9305f723c4cc")]
        public readonly InputSlot<float> Displacement = new InputSlot<float>();

        [Input(Guid = "c364b443-300e-4323-b0cc-f615649995a5")]
        public readonly InputSlot<float> BlurRadius = new InputSlot<float>();

        [Input(Guid = "1e6d0245-b5d4-45a5-8dac-6d905837060d")]
        public readonly InputSlot<float> SampleDistance = new InputSlot<float>();

        [Input(Guid = "4ed2a94d-ea7b-4fc5-8b3b-2ced9542b763")]
        public readonly InputSlot<float> Twist = new InputSlot<float>();

        [Input(Guid = "62c7b815-b189-4cd7-bad2-bb1c4251e646")]
        public readonly InputSlot<float> Zoom = new InputSlot<float>();

        [Input(Guid = "c0174be3-4a8c-4b06-a75d-e8c686b072af")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "19e5420c-6d48-4d5b-a0db-e2ee361f8f31")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "ffcdb058-5131-4146-8deb-a3102349e1c3")]
        public readonly InputSlot<float> DisplaceOffset = new InputSlot<float>();

        [Input(Guid = "19f7822f-e8df-4990-950b-2a39f62c59c0")]
        public readonly InputSlot<float> ShiftHue = new InputSlot<float>();

        [Input(Guid = "f153041e-6542-4197-a192-6f46e951f8df")]
        public readonly InputSlot<float> ShiftSaturation = new InputSlot<float>();

        [Input(Guid = "54ecb0e0-6fc0-4441-b26b-b820e95475a0")]
        public readonly InputSlot<float> ShiftBrightness = new InputSlot<float>();

        [Input(Guid = "c027bc94-4ef8-4828-8df5-d30abbd31cbc")]
        public readonly InputSlot<float> AmplifyEdges = new InputSlot<float>();

        [Input(Guid = "4232af30-de5f-4fd6-af92-d704e26f6850")]
        public readonly InputSlot<System.Numerics.Vector2> LuminosityRange = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "0eeae01b-80d6-4267-bdf5-a6df2533ffe9")]
        public readonly InputSlot<System.Numerics.Vector2> ChromaRange = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "c7f25ad5-1907-4851-8057-7c73f82da1be")]
        public readonly InputSlot<float> RangeClamping = new InputSlot<float>();

        [Input(Guid = "e05425c3-21fd-4621-b9ac-dbc972e26431")]
        public readonly InputSlot<float> TwirlNoise = new InputSlot<float>();

        [Input(Guid = "b481a27b-3715-4b0e-869e-a2b5093138b6")]
        public readonly InputSlot<float> TwirlNoiseScale = new InputSlot<float>();

        [Input(Guid = "f5fa4652-4292-4000-a29d-854d3a6f17fe")]
        public readonly InputSlot<float> TwirlNoiseSpeed = new InputSlot<float>();

        [Input(Guid = "3c93af8c-5658-4466-8301-95799b650e2b")]
        public readonly InputSlot<float> AddBlurred = new InputSlot<float>();

        [Input(Guid = "5264cbf8-13f6-4dfe-a82d-4f0904973488")]
        public readonly InputSlot<bool> Reset = new InputSlot<bool>();

        [Input(Guid = "e294f6ac-04c4-489b-8d26-615c8971ce11")]
        public readonly InputSlot<bool> IsEnabled = new InputSlot<bool>();

        [Input(Guid = "5a123784-082d-474c-9f00-0b5a7d3e8cfe")]
        public readonly InputSlot<int> BlurMethod = new InputSlot<int>();

}