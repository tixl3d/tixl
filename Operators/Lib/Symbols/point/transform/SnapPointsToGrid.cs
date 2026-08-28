namespace Lib.point.transform;

[Guid("bc88304a-a2c7-4df9-93d8-b7dfecbce3de")]
internal sealed class SnapPointsToGrid : Instance<SnapPointsToGrid>
{

    [Output(Guid = "b7bc82a2-f095-490a-91e3-276431d5eb87")]
    public readonly Slot<BufferWithViews> Output = new();

        [Input(Guid = "953a95d0-5226-46bb-80c3-f20b27a32064")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "3e1dbc97-76d9-47da-a12e-1aefa384cf81")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "40a48bac-c17e-4c28-80e1-7b2a51370aa8", MappedType = typeof(StrengthFactors))]
        public readonly InputSlot<int> AmountFactor = new InputSlot<int>();

        [Input(Guid = "8343ab8b-6d3a-48f7-93ea-c52e1c36a5fa", MappedType = typeof(SnapModes))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();

        [Input(Guid = "4d7f1f34-ca1b-43ee-803f-cbc14bcc8679")]
        public readonly InputSlot<float> GridScale = new InputSlot<float>();

        [Input(Guid = "eacc6bf8-1e12-44fb-8541-91ac4a557745")]
        public readonly InputSlot<System.Numerics.Vector3> GridStretch = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "04c5ee70-9f9a-4236-8871-772814b8b2ab")]
        public readonly InputSlot<System.Numerics.Vector3> Offset = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "a7d80f3e-298d-4eb5-9751-ec432cda4065")]
        public readonly InputSlot<float> Scatter = new InputSlot<float>();

        [Input(Guid = "f9df2ced-a40b-4679-8161-e2765bab7823")]
        public readonly InputSlot<System.Numerics.Vector2> BiasAndGain = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "bf7fef5e-a9d5-41e6-8025-0f74a9e07373")]
        public readonly InputSlot<bool> UseWAsWeight = new InputSlot<bool>();

        [Input(Guid = "c0328a18-cdce-4177-86f8-0242ccc1fb24")]
        public readonly InputSlot<bool> UseSelection = new InputSlot<bool>();

    private enum SnapModes
    {
        CenterDistance,
        CornersDistance,
        AxisCenterDistance,
        AxisEdgeDistance,
    }
    
    private enum StrengthFactors
    {
        None = 0,
        F1 = 1,
        F2 = 2,
    }
}