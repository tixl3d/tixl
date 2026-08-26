namespace Lib.image.generate.pattern;

[Guid("b882a5d0-f5ae-40a4-8c42-5b25e5775def")]
internal sealed class FraserGrid : Instance<FraserGrid>
{
    [Output(Guid = "de51ef93-049b-44df-9ea4-996929d8fa59")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "22d7ec5a-ba4e-4d2e-b15b-4de172b26e1e")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "6f4215a5-bd04-4151-b468-e06424c560a6")]
        public readonly InputSlot<System.Numerics.Vector4> Fill = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "0a7100e7-e67e-4a59-86a1-fcb872e8daaf")]
        public readonly InputSlot<System.Numerics.Vector4> FillB = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "df99e74d-4add-4600-a7c7-d6db74b0186a")]
        public readonly InputSlot<System.Numerics.Vector4> Background = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "84afcc08-74ae-4218-8250-2a0c72130f41")]
        public readonly InputSlot<float> Feather = new InputSlot<float>();

        [Input(Guid = "7551f216-17a7-4818-9cb0-6490e38fcb44")]
        public readonly InputSlot<System.Numerics.Vector2> Size = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "a49d6b13-2308-49bd-aac6-fde8f07e426d")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "52243bdc-6dce-46b9-95d9-af90cde3d56f")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "1dad9264-32a0-4476-a4af-c7eb9a5a5fec")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "40ea5583-313d-4a48-82f4-d52bfa7c9e17")]
        public readonly InputSlot<float> RotateShapes = new InputSlot<float>();

        [Input(Guid = "48008617-deee-459b-a492-b86f6cbde3df")]
        public readonly InputSlot<float> ShapeSize = new InputSlot<float>();

        [Input(Guid = "28111fbf-92ea-4674-9b65-0def5a7252cd")]
        public readonly InputSlot<float> BarWidth = new InputSlot<float>();

        [Input(Guid = "31d0e3ab-5aaf-4bf2-8d50-ac3c58026a51")]
        public readonly InputSlot<float> BorderWidth = new InputSlot<float>();

        [Input(Guid = "bf6d5734-8fa7-455f-a207-55a6501722e6")]
        public readonly InputSlot<float> RowSwift = new InputSlot<float>();

        [Input(Guid = "bde3ed03-b704-4948-8d54-886593aa148f")]
        public readonly InputSlot<float> RAffects_BarWidth = new InputSlot<float>();

        [Input(Guid = "a4a70b12-ab9b-4c53-a93c-6f2460228e1e")]
        public readonly InputSlot<float> GAffects_ShapeSize = new InputSlot<float>();

        [Input(Guid = "a5843a42-ed4f-4c11-8e23-e780b5bd453a")]
        public readonly InputSlot<float> BAffects_LineRatio = new InputSlot<float>();

        [Input(Guid = "75c859fa-9dad-4da3-994c-7232811d5ede")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();
}