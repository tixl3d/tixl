namespace Examples.Lib.field.use{
    [Guid("ba564ef5-bb9d-4b69-b3fe-65709cdd279e")]
    internal sealed class RaymarchPointsExample : Instance<RaymarchPointsExample>
    {
        [Output(Guid = "428d1d44-1a4d-49bb-ac5e-2011326e3ed7")]
        public readonly Slot<Command> Output = new Slot<Command>();


        [Input(Guid = "c0b0b4e5-1781-4417-8f2f-ffdc1392fef7")]
        public readonly InputSlot<ShaderGraphNode> InputField = new InputSlot<ShaderGraphNode>();

        [Input(Guid = "ee47e0a0-4855-4b07-839e-f23cb588aac0")]
        public readonly InputSlot<System.Numerics.Vector3> StartPosition = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "605074a4-eaf7-4638-84d3-4a37ac0e8ce2")]
        public readonly InputSlot<float> Angle = new InputSlot<float>();

        [Input(Guid = "6d232bb0-922f-48e1-9e2b-9043d444e416")]
        public readonly InputSlot<int> MaxSteps = new InputSlot<int>();

        [Input(Guid = "5ecede5e-287d-4f40-b84e-9efd7e36b0fa")]
        public readonly InputSlot<System.Numerics.Vector4> FieldColor = new InputSlot<System.Numerics.Vector4>();

    }
}

