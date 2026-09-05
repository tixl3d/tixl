namespace Lib.image.fx.blur;

[Guid("1192ae86-b174-4b58-9cc6-38afb666ce35")]
internal sealed class DirectionalBlur : Instance<DirectionalBlur>
{
    [Output(Guid = "c57e38ab-a802-498c-b0f7-cad86e6426a3")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "3cc9487b-bf18-416c-9d69-86592130bfa6")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "e94e2b7e-6f0a-42b5-bf5e-abdffe728273")]
        public readonly InputSlot<float> Size = new InputSlot<float>();

        [Input(Guid = "11cb8f1e-fac5-4623-b61c-d6482633e505")]
        public readonly InputSlot<float> Samples = new InputSlot<float>();

        [Input(Guid = "ab3206bf-9413-4f31-9c3d-0c1fe7477862")]
        public readonly InputSlot<float> Angle = new InputSlot<float>();

        [Input(Guid = "fd99fc97-43a8-4a04-8e17-95c2abc289fc")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> FxTextures = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "815975d2-5613-4ca8-b208-f09ac07b0518")]
        public readonly InputSlot<float> FxAngleFactor = new InputSlot<float>();

        [Input(Guid = "dfa5e772-0cb4-440f-8483-dcc89b40107d")]
        public readonly InputSlot<float> FxSizeFactor = new InputSlot<float>();

        [Input(Guid = "b295586d-2339-4d69-868a-c17468e77998")]
        public readonly InputSlot<bool> RefinementPass = new InputSlot<bool>();

        [Input(Guid = "a3af8d20-8c09-4250-b5bd-806c12ad7c05")]
        public readonly InputSlot<int> RefinementSamples = new InputSlot<int>();

        [Input(Guid = "ea812b8d-f275-4a91-bfae-cd3308c04362")]
        public readonly InputSlot<float> RefineSizeFactor = new InputSlot<float>();

        [Input(Guid = "6c0cca14-6a0e-4a04-a67b-cbb134d90d03")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> Wrap = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "94099125-52d7-475d-aff2-bb1bbd0bd30a")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();
}