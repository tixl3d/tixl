namespace Lib.image.analyze;

[Guid("592a2b6f-d4e3-43e0-9e73-034cca3b3900")]
internal sealed class ImageLevels : Instance<ImageLevels>
{
    [Output(Guid = "ae9ebfa0-3528-489b-9c07-090f26dd6968")]
    public readonly Slot<Texture2D> Output = new();

        [Input(Guid = "f434bac8-b7d8-4787-adf2-1782d6588da8")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture2d = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "1224b62e-5fca-41e9-a388-4c13c1458d56")]
        public readonly InputSlot<System.Numerics.Vector2> Center = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "f1084d72-f8b8-4723-82be-e1e98880faf3")]
        public readonly InputSlot<float> Rotation = new InputSlot<float>();

        [Input(Guid = "48e80f45-9685-4ded-aa1c-d05e16658c5a")]
        public readonly InputSlot<float> Width = new InputSlot<float>();

        [Input(Guid = "8910ac23-551b-446a-b833-98f4efce1022")]
        public readonly InputSlot<System.Numerics.Vector2> Range = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "a8a4d660-7356-40de-8dc6-549a72b69973")]
        public readonly InputSlot<float> ShowOriginal = new InputSlot<float>();
}