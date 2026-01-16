namespace Lib.image.use;

[Guid("46daab0e-e957-413e-826c-0699569d0e07")]
internal sealed class CustomPixelShader : Instance<CustomPixelShader>
{

        [Input(Guid = "5f90f885-0ccc-4014-a921-dc710257835a")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> FxTexture = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "e63cf24c-0e01-47d4-9532-18261310315e")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> FxTexture2 = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "8c3ffefe-8721-4dde-b252-22eb8be02d3f")]
        public readonly InputSlot<string> ShaderCode = new InputSlot<string>();

        [Input(Guid = "674cabbd-cf31-46ac-9a1a-4f6bd727c977")]
        public readonly InputSlot<System.Numerics.Vector2> Center = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "3d84725a-594b-46d8-aa21-eec99026115d")]
        public readonly InputSlot<float> A = new InputSlot<float>();

        [Input(Guid = "b4895a95-5ff4-4583-9ec3-befcf0f7b18b")]
        public readonly InputSlot<float> B = new InputSlot<float>();

        [Input(Guid = "60bdd684-8005-4576-b09b-1b5d6124da1d")]
        public readonly InputSlot<float> C = new InputSlot<float>();

        [Input(Guid = "db522fd4-5cfc-49f6-9983-02ec0dd6090a")]
        public readonly InputSlot<float> D = new InputSlot<float>();

        [Input(Guid = "83e06d04-02bd-40cc-8666-d5dd62a9e63e")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "e0d05b9c-d433-4b9d-8e70-db2f7993d628")]
        public readonly InputSlot<SharpDX.DXGI.Format> TextureFormat = new InputSlot<SharpDX.DXGI.Format>();

        [Input(Guid = "92fdfa08-e7da-4a71-9145-277b74c93729")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();

        [Input(Guid = "a965b7d3-c78f-4da7-ae70-c461cc9b173c")]
        public readonly InputSlot<bool> Clear = new InputSlot<bool>();

        [Input(Guid = "c9a801ec-13fb-4ad4-b0cd-d125b5db500a")]
        public readonly InputSlot<string> AdditionalCode = new InputSlot<string>();

        [Input(Guid = "fb8d51fe-b4c2-452a-9e53-b649aed92bd7")]
        public readonly InputSlot<bool> IgnoreTemplate = new InputSlot<bool>();

        [Input(Guid = "b898c5a9-1c4b-4958-a7c1-01c27da10f6a")]
        public readonly MultiInputSlot<SharpDX.Direct3D11.ShaderResourceView> ShaderResources = new MultiInputSlot<SharpDX.Direct3D11.ShaderResourceView>();

        [Input(Guid = "2dc4d202-2ee5-4d4d-b1b2-234a0b21ab94")]
        public readonly MultiInputSlot<SharpDX.Direct3D11.Buffer> ConstantBuffers = new MultiInputSlot<SharpDX.Direct3D11.Buffer>();

        [Input(Guid = "8ea35e89-137a-4cb3-b26d-ba6bd9e5ce12")]
        public readonly MultiInputSlot<SharpDX.Direct3D11.SamplerState> CustomSampler = new MultiInputSlot<SharpDX.Direct3D11.SamplerState>();

    [Output(Guid = "12fcfd9e-1c2f-46fc-b570-83b93ec7d101")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Output(Guid = "163afb2e-2c9d-46b6-8959-a7736912d3f5")]
        public readonly Slot<string> ShaderCode_ = new Slot<string>();
}