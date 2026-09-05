using T3.Core.Utils;

namespace Lib.image.generate.basic;

[Guid("27b0e1af-cb2e-4603-83f9-5c9b042d87e6")]
internal sealed class Blob : Instance<Blob>
{
    [Output(Guid = "b882de23-5b94-4791-af13-e195211cffb3")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "8cc15ea0-074f-40ed-813d-b93f48681094")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "d2b0dd99-c289-4c1b-9335-c29a6b4a6ba3")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "fd05c355-7afa-4af6-9529-d4071d145d3b")]
        public readonly InputSlot<System.Numerics.Vector4> Background = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "a9d505ce-2cb1-4911-acc3-c509e1eebc2b", MappedType = typeof(SharedEnums.RgbBlendModes))]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "33f31c62-b0ea-42f9-a226-d0f5154731ee")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "37da22d0-56ca-444a-9c9d-27a70283b7c0")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "77544b82-e897-4e69-bfaf-e861891d1fd4")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "f0c128b1-27a1-42e0-a8a4-6fd94d527c05")]
        public readonly InputSlot<float> Feather = new InputSlot<float>();

        [Input(Guid = "0c49c872-852a-4f15-8cde-f3cda743c28e")]
        public readonly InputSlot<float> FeatherBias = new InputSlot<float>();

        [Input(Guid = "7daacb43-54de-47d2-afcd-694f6afce59d")]
        public readonly InputSlot<System.Numerics.Vector2> Position = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "89ca8093-8d13-471d-bfb4-613b13bc084d")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();

        [Input(Guid = "e45f325d-cf2d-4972-aea6-9545aec66ea7")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "fb6ff541-7ffe-4d3e-8b23-2f812f54f689")]
        public readonly InputSlot<SharpDX.DXGI.Format> TextureFormat = new InputSlot<SharpDX.DXGI.Format>();
}