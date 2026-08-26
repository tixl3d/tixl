namespace Lib.image.generate.pattern;

[Guid("3f955def-cf86-4bd4-be98-ff1adea8c495")]
internal sealed class ValueRaster : Instance<ValueRaster>
{
    [Output(Guid = "1f33eae1-7e0f-44ee-8a63-df7502443acb")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "03fa6f20-7bdc-4af2-8ad7-d95cd58d4e69")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "97e4103b-72ba-4d54-a41a-ac6c34d85878")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "057bd1b7-c4ef-44da-b180-172810265569")]
        public readonly InputSlot<System.Numerics.Vector4> Background = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "8c5601d2-a39a-4a02-9fef-0ebe0dc8330e")]
        public readonly InputSlot<float> MixOriginal = new InputSlot<float>();

        [Input(Guid = "e8f46ada-11da-4212-8751-5793b95cbb8d")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "317deea3-2d0d-4493-9008-6c0102a9a7e3")]
        public readonly InputSlot<System.Numerics.Vector2> RangeX = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "3037801b-e7c8-4aa1-9c23-5c815d5eb02e")]
        public readonly InputSlot<System.Numerics.Vector2> RangeY = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "d2168776-0cbb-4ede-85e6-731d50b14d28")]
        public readonly InputSlot<float> MajorLineWidth = new InputSlot<float>();

        [Input(Guid = "00915862-77b3-4205-8c6a-3a51f4e9cc41")]
        public readonly InputSlot<float> MinorLineWidth = new InputSlot<float>();

        [Input(Guid = "245070c1-af95-47c9-b697-2876c1097ccb")]
        public readonly InputSlot<System.Numerics.Vector2> Density = new InputSlot<System.Numerics.Vector2>();
}