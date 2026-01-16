namespace Lib.point.transform;

[Guid("acc71a14-daad-4b36-b0bc-cf0a796cc5d9")]
internal sealed class OrientPoints : Instance<OrientPoints>
{

    [Output(Guid = "23a08560-9764-42a1-a889-dd8839476747")]
    public readonly Slot<BufferWithViews> Output = new();

        [Input(Guid = "865ad090-0fdd-4683-ba93-b6be92b55cb3")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "4fec5414-16a2-4b48-9605-1bc3e7f464b5")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "d7867948-ae1b-48cf-b66b-19d16f1a458c", MappedType = typeof(FModes))]
        public readonly InputSlot<int> AmountFactor = new InputSlot<int>();

        [Input(Guid = "607fd90d-57f3-4a6a-b843-86c7170c854c")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "2aa74709-65f3-49fa-9890-f0a0f6e76bbf")]
        public readonly InputSlot<System.Numerics.Vector3> UpVector = new InputSlot<System.Numerics.Vector3>();

    
        [Input(Guid = "02ae76ba-7be8-4112-a59b-55616343f1dd")]
        public readonly InputSlot<bool> Flip = new InputSlot<bool>();
        
        [Input(Guid = "4358e71b-3f33-4868-af4d-97e8e04087a6")]
        public readonly InputSlot<bool> WIsWeight = new InputSlot<bool>();
        
        private enum FModes
        {
            None,
            F1,
            F2,
        }
}