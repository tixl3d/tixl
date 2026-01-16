namespace Lib.image.fx.distort;

[Guid("1b149f1f-529c-4418-ac9d-3871f24a9e38")]
internal sealed class Displace : Instance<Displace>
{
    [Output(Guid = "0faa056c-b1d6-4e1f-a9be-b0791f3bae84")]
    public readonly Slot<Texture2D> Output = new();

        [Input(Guid = "d0508dfa-89cf-4713-8f5e-893dd5bfc3f4")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "3b5b278d-fd4e-4216-9916-5cd7ffd54ab2")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> DisplaceMap = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "6a5c120f-7c04-439b-ad2d-6f78ceb3b378", MappedType = typeof(DisplaceModes))]
        public readonly InputSlot<int> DisplaceMode = new InputSlot<int>();

        [Input(Guid = "0f2867ab-a65e-4bf3-b1b5-9c241690ba5f")]
        public readonly InputSlot<float> Displacement = new InputSlot<float>();

        [Input(Guid = "0eff3a75-eafc-4a5e-8a2c-10577c12e776")]
        public readonly InputSlot<float> DisplacementOffset = new InputSlot<float>();

        [Input(Guid = "dc8dfa33-1a49-4800-8c1f-89b29d7427f3")]
        public readonly InputSlot<float> Twist = new InputSlot<float>();

        [Input(Guid = "77673c64-918d-46a6-aa29-c362057afee6")]
        public readonly InputSlot<float> Shade = new InputSlot<float>();

        [Input(Guid = "404c8102-3a5e-45c6-a0f1-4d97a5f0db07")]
        public readonly InputSlot<float> SampleRadius = new InputSlot<float>();

        [Input(Guid = "1ff7d454-b8fb-470f-beee-ec7521db8a7f")]
        public readonly InputSlot<System.Numerics.Vector2> DisplaceMapOffset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "6e772174-813d-4baa-b6b5-27e197b547ac")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> WrapMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "b2f58dc7-e5c6-4c57-a704-94aaa0b1e002")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();

        [Input(Guid = "ea2b9f80-49b9-4c90-ba34-f0274169ece3")]
        public readonly InputSlot<bool> RGSS_4xAA = new InputSlot<bool>();

        [Input(Guid = "0c0dd89a-e9ff-4746-b12c-dd4799a0b500")]
        public readonly InputSlot<SharpDX.Direct3D11.Filter> TextureFiltering = new InputSlot<SharpDX.Direct3D11.Filter>();
        
    private enum DisplaceModes {
        IntensityGradient,
        Intensity,
        NormalMap,
        SignedNormalMap,
    }
}