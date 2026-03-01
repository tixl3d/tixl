namespace Lib.point.transform;

[Guid("20eee636-7bd4-44e8-8a86-77fe4be1f50b")]
internal sealed class IkChain :Instance<IkChain>
{
    [Output(Guid = "69c450f9-5f20-422c-a5cd-82647effbdbf")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

   
   

   

        [Input(Guid = "824ab47a-93ae-432b-8f5a-e701251b0e36")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();
    
    [Input(Guid = "B7B18E95-F9A7-4BCA-8960-D2B3A535E394")]
    public readonly InputSlot<float> Tolerance = new InputSlot<float>();
  
    [Input(Guid = "EE7BE099-7DFB-4C4E-98AD-0AE739CF9F5C")]
    public readonly InputSlot<int> MaxIterations = new();

        [Input(Guid = "6c2c8f16-c874-44f7-bc39-bbf770546479")]
        public readonly InputSlot<bool> Reset = new InputSlot<bool>();

        [Input(Guid = "de07cd9c-11e2-414d-9c5c-9e38cc51d80e")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Targets = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "7b201b0d-8298-4ddd-b534-b8127e4c5581")]
        public readonly InputSlot<float> Influence = new InputSlot<float>();

        [Input(Guid = "1edbc9d8-e003-4337-b8f7-179068a11dff")]
        public readonly InputSlot<int> PointsPerChain = new InputSlot<int>();

        [Input(Guid = "83518060-732c-421e-b90a-97c325882fd6")]
        public readonly InputSlot<int> AngleConstraint = new InputSlot<int>();

        [Input(Guid = "a47682b7-98fe-4461-a328-f9c8c14ef137")]
        public readonly InputSlot<float> MaxBendAngle = new InputSlot<float>();


}