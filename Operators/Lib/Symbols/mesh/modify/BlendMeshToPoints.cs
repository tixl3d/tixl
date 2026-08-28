namespace Lib.mesh.modify;

[Guid("109b1ca2-7b4c-4535-9cc6-8f176c4207dd")]
internal sealed class BlendMeshToPoints :Instance<BlendMeshToPoints>{

    [Output(Guid = "572caee7-97c7-4b0e-9625-37c8910f3f5e")]
    public readonly Slot<MeshBuffers> BlendedMesh = new();

        [Input(Guid = "4ed67387-8b51-4292-af31-0846b18389f4")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "09bd1214-a53c-4632-a64b-85476d74f44c")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "0e54a6e8-07ac-45e1-9005-819a599e2b8a")]
        public readonly InputSlot<float> BlendValue = new InputSlot<float>();

        [Input(Guid = "c5dba0b7-9911-440c-9945-0e5ab907c456")]
        public readonly InputSlot<float> Scatter = new InputSlot<float>();

        [Input(Guid = "2d2c8c8b-a197-4834-b8da-7aa8f715b763", MappedType = typeof(BlendModes))]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "c7aa442e-6a67-40d3-92a7-981d59f5176e")]
        public readonly InputSlot<float> RangeWidth = new InputSlot<float>();

        [Input(Guid = "937f2e7a-87f8-4f87-87d0-729c3134c4bc", MappedType = typeof(PairingModes))]
        public readonly InputSlot<int> Pairing = new InputSlot<int>();

        
    private enum BlendModes
    {
        Blend,
        UseSelectedAsWeight,
        UseF1AsWeight,
        UseF2AsWeight,
        RangeBlend,
        RangeBlendSmooth,
    }
        
    private enum PairingModes
    {
        WrapAround,
        Adjust,
    }
        
}