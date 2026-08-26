namespace Lib.image.fx.blur;

[Guid("f634e126-8834-46ea-bd6e-5ebfdc8b0733")]
internal sealed class Bloom : Instance<Bloom>
{

        [Output(Guid = "f3fa372d-f037-48fd-8a8d-a0135b4c20cb")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> Result = new Slot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "97d8f330-5957-4309-8c56-d94c1266f6cb")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "bb706662-2555-4f3b-a81e-60e04f052f36")]
        public readonly InputSlot<float> Intensity = new InputSlot<float>();

        [Input(Guid = "68e5fe43-84b6-46da-8e74-0576d77d49b6")]
        public readonly InputSlot<System.Numerics.Vector4> ColorWeights = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "28e0b719-0888-4ef6-85d9-bbd75f7a4537")]
        public readonly InputSlot<float> Threshold = new InputSlot<float>();

        [Input(Guid = "be4e0dba-7613-4860-96e1-fe7ea493511f")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> GlowGradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "355fa01e-b2c4-4d11-bead-18c5961d8e96")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "2064aba1-658e-4795-a4fc-5be1026d7064")]
        public readonly InputSlot<int> MaxLevels = new InputSlot<int>();

        [Input(Guid = "c6a0cadc-9e1c-40ac-97f1-d9271b5376df")]
        public readonly InputSlot<float> Blur = new InputSlot<float>();

        [Input(Guid = "e7412f9b-b5e3-4166-8f5e-4b142ccee55a")]
        public readonly InputSlot<bool> Clamp = new InputSlot<bool>();

}