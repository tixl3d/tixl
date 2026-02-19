namespace Lib.mesh.modify;

[Guid("058323b6-2e4c-4448-a4c4-ebbc878b3aae")]
internal sealed class SelectVerticesWithSDF :Instance<SelectVerticesWithSDF>{

        [Output(Guid = "49e13584-2ff7-4854-82e6-adcb501b7b20")]
        public readonly Slot<T3.Core.DataTypes.MeshBuffers> ResultMesh = new Slot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "4ad9bd49-08a3-46ce-99a8-39634eb7e6ee")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "a4c1c8a0-237c-4d96-b196-55af34530ede")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> SdfField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "2b60f260-d9f8-43ec-a6a5-0d49f44932c0")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "015d6833-6ffb-431e-b5ec-80483eb2b9b1", MappedType = typeof(FModes))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();

        [Input(Guid = "c4b3ad25-5e0b-4eeb-a9a5-583b33840458", MappedType = typeof(FModes))]
        public readonly InputSlot<int> WriteTo = new InputSlot<int>();

        [Input(Guid = "1140dc1e-430a-4a29-806e-6fb0b8eccaad", MappedType = typeof(Modes))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();

        [Input(Guid = "fb9e8438-ad0c-47ff-aa74-c6405eca701d", MappedType = typeof(MappingModes))]
        public readonly InputSlot<int> Mapping = new InputSlot<int>();

        [Input(Guid = "8097ce0c-140a-4f90-b1ff-9fdf8640d8e1")]
        public readonly InputSlot<float> Range = new InputSlot<float>();

        [Input(Guid = "7ddf82f1-75b5-489f-9ca1-05d3fb05a6b8")]
        public readonly InputSlot<float> Offset = new InputSlot<float>();

        [Input(Guid = "e69d5c8f-816d-4aca-b0b1-cde907fa396c")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "6a942084-123d-4594-bd18-c856f67c1425")]
        public readonly InputSlot<float> Scatter = new InputSlot<float>();

        [Input(Guid = "f1dd8530-5c97-48f1-839c-711b6a42e222")]
        public readonly InputSlot<bool> ClampNegative = new InputSlot<bool>();

        [Input(Guid = "92a40391-62af-480d-854f-b69a272d3e8d")]
        public readonly InputSlot<bool> DiscardNonSelected = new InputSlot<bool>();



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