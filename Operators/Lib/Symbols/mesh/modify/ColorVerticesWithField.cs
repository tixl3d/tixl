namespace Lib.mesh.modify;

[Guid("501d75f4-c789-4946-a7ed-cca9245d38ef")]
internal sealed class ColorVerticesWithField :Instance<ColorVerticesWithField>{

        [Output(Guid = "3837931f-00d5-47d6-84e8-3e9cd3e3d252")]
        public readonly Slot<T3.Core.DataTypes.MeshBuffers> OutMesh = new Slot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "d8c32b3b-8d88-448c-9af5-8e9e1cb9884e")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "f6064029-0a27-4598-9766-0bfe0e2e4eb4")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> SdfField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "e0537ef5-5d77-48dd-8b53-043fc7ec1338")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "f61722bb-11f3-4eb9-bb02-ca6161875492", MappedType = typeof(FModes))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();



    private enum Modes
    {
        Override,
        Add,
        Sub,
        Multiply,
        Invert,
    }

    private enum FModes
    {
        None,
        F1,
        F2,
    }
    
    private enum MappingModes
    {
        Centered,
        FromStart,
        PingPong,
        Repeat,
    }
}