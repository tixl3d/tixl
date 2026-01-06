using T3.Core.Utils;

namespace Lib.mesh.draw;

[Guid("42cb88bc-beb8-4d89-ac99-44b77be5f03e")]
internal sealed class DrawMeshAtPoints : Instance<DrawMeshAtPoints>
{
    [Output(Guid = "774a96e4-24e2-4e1a-a70d-63794d24dd51", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

        [Input(Guid = "408ae7c7-9aa8-4537-8c55-b5689f8f9b56")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "329d8248-5f9f-4ad3-9b97-0f142e91ba05")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "0af05ab4-0d77-4f01-a79b-691f58f702ef")]
        public readonly InputSlot<float> Size = new InputSlot<float>();

        [Input(Guid = "998e0e5b-fccf-430c-b799-aacc8b0cbb28", MappedType = typeof(ScaleFXModes))]
        public readonly InputSlot<int> ScaleFactor = new InputSlot<int>();

        [Input(Guid = "69e1f021-4db2-43a1-a4e6-837024350dc1")]
        public readonly InputSlot<bool> UsePointScale = new InputSlot<bool>();

        [Input(Guid = "ba7befdf-270b-4ac0-bfc2-7543e2c3097b")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "a8590e4f-2edf-42c9-8bc2-e7b521f8cafc")]
        public readonly InputSlot<float> AlphaCutOff = new InputSlot<float>();

        [Input(Guid = "6c36bf68-e22f-419d-9ec0-f60a83d6a560", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "22b0817f-3149-4713-b87b-89c54300cde8", MappedType = typeof(FillMode))]
        public readonly InputSlot<int> FillMode = new InputSlot<int>();

        [Input(Guid = "c0351f55-ad27-4fbd-b3d5-668ff49f0ea0")]
        public readonly InputSlot<SharpDX.Direct3D11.CullMode> CullMode = new InputSlot<SharpDX.Direct3D11.CullMode>();

        [Input(Guid = "59420e45-cbe2-48d8-961c-3dea5e55b182", MappedType = typeof(ShadingModes))]
        public readonly InputSlot<int> Shading = new InputSlot<int>();

        [Input(Guid = "9ec6c311-22de-411b-9821-0d458f33df7a")]
        public readonly InputSlot<float> SpecularAA = new InputSlot<float>();

        [Input(Guid = "dd511aab-0472-4109-9c10-cc1ab5042499")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "70f4cc27-f901-4faa-aa2e-b4cd2a50ff73")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "a7cb95c4-fb81-49c7-9503-a45589713415")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> ColorField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();
        
        private enum ScaleFXModes
        {
            None = 0,
            F1 = 1,
            F2 = 2,
        }
    private enum ShadingModes
    {
        Default = 0,
        Flat = 1,
    }
}