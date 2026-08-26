namespace Lib.image.fx.distort;

[Guid("2be3d25a-777f-4464-a8c7-351d9e38c30d")]
internal sealed class TimeDisplace : Instance<TimeDisplace>
{
    [Output(Guid = "60e4f8cd-08a1-40af-b98d-a6edfe3b63d7")]
    public readonly Slot<Texture2D> Output = new();

        [Input(Guid = "6bd85d4b-62ef-4f62-95ac-2d5136997e7e")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "d0a62af1-75b9-4cc6-8ed7-69169c74c183")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> DisplaceMap = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "67ff9326-ea4a-4cd1-80eb-8bd490cf8938", MappedType = typeof(DisplaceModes))]
        public readonly InputSlot<int> DisplaceMode = new InputSlot<int>();

        [Input(Guid = "10927384-3f0f-4411-ac00-08e15f047a97")]
        public readonly InputSlot<float> Displacement = new InputSlot<float>();

        [Input(Guid = "222e760e-4f44-4abf-bd4f-6a8510ddf82d")]
        public readonly InputSlot<float> DisplacementOffset = new InputSlot<float>();

        [Input(Guid = "24228c12-5be7-48a6-8989-e76b4bce6594")]
        public readonly InputSlot<float> Twist = new InputSlot<float>();

        [Input(Guid = "de5a6566-d625-4315-bda6-498ba14110a1")]
        public readonly InputSlot<float> Shade = new InputSlot<float>();

        [Input(Guid = "bba49085-dc3b-482e-8e34-07ce49184235")]
        public readonly InputSlot<float> SampleRadius = new InputSlot<float>();

        [Input(Guid = "2172181c-64cb-4fa7-be4e-b4c40e2b1fad")]
        public readonly InputSlot<System.Numerics.Vector2> DisplaceMapOffset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "e65f6f8e-293a-403d-8824-75d9040265ef")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> WrapMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "43259a11-5da7-402d-a5cf-1fcac3ce5d36")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();

        [Input(Guid = "700a8d59-3efd-4a60-980e-be68c9702d96")]
        public readonly InputSlot<bool> RGSS_4xAA = new InputSlot<bool>();

        [Input(Guid = "8e656ce6-3eb0-4dc4-bfdf-b278b9e27ff3")]
        public readonly InputSlot<SharpDX.Direct3D11.Filter> TextureFiltering = new InputSlot<SharpDX.Direct3D11.Filter>();
        
    private enum DisplaceModes {
        IntensityGradient,
        Intensity,
        NormalMap,
        SignedNormalMap,
    }
}