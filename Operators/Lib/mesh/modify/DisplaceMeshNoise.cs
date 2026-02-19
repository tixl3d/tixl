namespace Lib.mesh.modify;

[Guid("b5709297-c714-4019-9d0b-6982590b5590")]
internal sealed class DisplaceMeshNoise : Instance<DisplaceMeshNoise>
{

    [Output(Guid = "b91689eb-4274-4534-9f95-515a93c57ebe")]
    public readonly Slot<MeshBuffers> Result = new();

        [Input(Guid = "fb86f0d6-1e5c-478f-b723-9f9462e2966c")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> InputMesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "b7559321-2dbe-4fe0-ab86-52532d008980")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "4b1a66a4-b5e4-4bc3-97f5-bd3cda668893")]
        public readonly InputSlot<float> Frequency = new InputSlot<float>();

        [Input(Guid = "e5213b00-6302-45e1-a172-5a11bd91892e")]
        public readonly InputSlot<float> Variation = new InputSlot<float>();

        [Input(Guid = "b89b6730-de46-4d56-b2a8-b7d6f6876620")]
        public readonly InputSlot<float> Phase = new InputSlot<float>();

        [Input(Guid = "e02379a9-fadf-46d4-a6c2-74264e91b5a6")]
        public readonly InputSlot<System.Numerics.Vector3> AmountDistribution = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "08e2222f-6de1-46a8-bbdc-da83251f424e")]
        public readonly InputSlot<float> RotationLookupDistance = new InputSlot<float>();

        [Input(Guid = "9a82f2a6-390e-4073-bc80-e5fd3b1c0bfe")]
        public readonly InputSlot<float> UseWAsWeight = new InputSlot<float>();

        [Input(Guid = "357ED675-212F-4B2C-B93B-C8460867A9AE", MappedType = typeof(Spaces))]
        public readonly InputSlot<int> Space = new InputSlot<int>();

        [Input(Guid = "f108f6f7-5e6f-43c8-9d0b-c2e7bf5adf9c", MappedType = typeof(Directions))]
        public readonly InputSlot<int> Direction = new InputSlot<int>();

        [Input(Guid = "093a468c-c208-4caf-be4f-d5d7d9ceddeb")]
        public readonly InputSlot<float> OffsetDirection = new InputSlot<float>();

        [Input(Guid = "83cb775f-c600-41c9-9435-604f77a426bd")]
        public readonly InputSlot<bool> UseVertexSelection = new InputSlot<bool>();

        
    private enum Spaces
    {
        PointSpace,
        WorldSpace,
        ObjectSpace,
    }
        
    private enum Directions
    {
        WorldSpace,
        SurfaceNormal,
    }
}