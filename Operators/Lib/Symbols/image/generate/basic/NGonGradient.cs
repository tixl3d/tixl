using T3.Core.Utils;

namespace Lib.image.generate.basic;

[Guid("05463270-37d4-400f-8d0d-c50f81663304")]
internal sealed class NGonGradient : Instance<NGonGradient>
{
    [Output(Guid = "2cf9e9eb-9f08-43d3-bcde-7a05df13969b")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "ee415be6-d478-44a8-b6b5-f315d1fde694")]
        public readonly InputSlot<System.Numerics.Vector2> Position = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "15a72217-4a3e-4e03-a767-0494ea1943f7")]
        public readonly InputSlot<float> Sides = new InputSlot<float>();

        [Input(Guid = "b96527a6-0392-4e50-aa17-564976141290")]
        public readonly InputSlot<float> Radius = new InputSlot<float>();

        [Input(Guid = "1264ebb1-f953-46f9-9915-d0fca5a72aa8")]
        public readonly InputSlot<float> Curvature = new InputSlot<float>();

        [Input(Guid = "3493fb54-55a1-497d-8eb0-4a8fa5b92a9c")]
        public readonly InputSlot<float> Roundness = new InputSlot<float>();

        [Input(Guid = "d3cf9f75-ec08-4162-bad6-514173ef974c")]
        public readonly InputSlot<float> Blades = new InputSlot<float>();

        [Input(Guid = "49066387-92a6-46b2-a471-be0104e70651")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "08937f41-a722-4d5b-8cf6-0b7d48323af4")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "134a9879-54f7-4b9c-8494-195a159d6428")]
        public readonly InputSlot<float> Width = new InputSlot<float>();

        [Input(Guid = "d926fc8b-c42d-4880-8cfc-f940f7dafb03")]
        public readonly InputSlot<float> Offset = new InputSlot<float>();

        [Input(Guid = "e16b0bb7-2d7b-49a1-9cb4-b7f71ace7cc5")]
        public readonly InputSlot<bool> PingPong = new InputSlot<bool>();

        [Input(Guid = "94d5a7cd-99bb-4146-a556-dd893eb3ebbc")]
        public readonly InputSlot<bool> Repeat = new InputSlot<bool>();

        [Input(Guid = "df44d68a-1631-4f05-a616-242fab419e57")]
        public readonly InputSlot<System.Numerics.Vector2> BiasAndGain = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "60a0fd35-a64a-4710-b7ba-68d03887ca14", MappedType = typeof(SharedEnums.RgbBlendModes))]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "4bfe03f0-f1c3-47c0-98c3-873087e40c17")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "3bc236ab-c5f8-4dee-b933-84cf627118ef")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();
}